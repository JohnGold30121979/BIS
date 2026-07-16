using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BIS.ERP.Updater;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static async Task<int> Main(string[] args)
    {
        var planPath = GetArgumentValue(args, "--plan");
        if (string.IsNullOrWhiteSpace(planPath))
            return 2;

        var logPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(planPath)) ?? AppContext.BaseDirectory, "update-log.txt");

        try
        {
            await LogAsync(logPath, "Updater started.");

            var plan = JsonSerializer.Deserialize<AppUpdatePlan>(
                await File.ReadAllTextAsync(planPath, Encoding.UTF8),
                JsonOptions) ?? throw new InvalidOperationException("Файл плана обновления поврежден.");

            await UpdateHistoryAsync(plan, "Applying", null, string.Empty);
            await WaitForMainProcessAsync(plan.MainProcessId, logPath);
            await ApplyFilesAsync(plan, logPath);
            await UpdateHistoryAsync(plan, "Applied", DateTime.UtcNow, string.Empty);
            await LogAsync(logPath, "Update applied successfully.");
            StartApplication(plan, logPath);
            return 0;
        }
        catch (Exception ex)
        {
            await LogAsync(logPath, $"Update failed: {ex}");
            await TryMarkFailedAsync(planPath, ex.Message);
            await TryRestartFromPlanAsync(planPath, logPath);
            return 1;
        }
    }

    private static async Task WaitForMainProcessAsync(int processId, string logPath)
    {
        if (processId <= 0)
            return;

        try
        {
            var process = Process.GetProcessById(processId);
            await LogAsync(logPath, $"Waiting for main process {processId}...");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await process.WaitForExitAsync(timeout.Token);
            await LogAsync(logPath, "Main process exited.");
        }
        catch (ArgumentException)
        {
            await LogAsync(logPath, "Main process is already closed.");
        }
    }

    private static async Task ApplyFilesAsync(AppUpdatePlan plan, string logPath)
    {
        var sourceRoot = GetFullDirectory(plan.SourceDirectory);
        var targetRoot = GetFullDirectory(plan.TargetDirectory);
        var backupRoot = GetFullDirectory(plan.BackupDirectory);

        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Папка файлов обновления не найдена: {sourceRoot}");

        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(backupRoot);

        var copiedFiles = new List<CopiedFile>();

        try
        {
            foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(sourceRoot, sourceFile));
                var targetFile = GetSafePath(targetRoot, relativePath);
                var backupFile = GetSafePath(backupRoot, relativePath);
                var existed = File.Exists(targetFile);

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);

                if (existed)
                    File.Copy(targetFile, backupFile, overwrite: true);

                File.Copy(sourceFile, targetFile, overwrite: true);
                copiedFiles.Add(new CopiedFile(targetFile, backupFile, existed));
                await LogAsync(logPath, $"Copied: {relativePath}");
            }
        }
        catch
        {
            await RollbackAsync(copiedFiles, logPath);
            throw;
        }
    }

    private static async Task RollbackAsync(List<CopiedFile> copiedFiles, string logPath)
    {
        await LogAsync(logPath, "Rollback started.");

        foreach (var item in copiedFiles.AsEnumerable().Reverse())
        {
            try
            {
                if (item.HadExistingFile && File.Exists(item.BackupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);
                    File.Copy(item.BackupPath, item.TargetPath, overwrite: true);
                    await LogAsync(logPath, $"Restored: {item.TargetPath}");
                }
                else if (!item.HadExistingFile && File.Exists(item.TargetPath))
                {
                    File.Delete(item.TargetPath);
                    await LogAsync(logPath, $"Removed new file: {item.TargetPath}");
                }
            }
            catch (Exception ex)
            {
                await LogAsync(logPath, $"Rollback warning for {item.TargetPath}: {ex.Message}");
            }
        }
    }

    private static void StartApplication(AppUpdatePlan plan, string logPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(plan.RestartExecutable) || !File.Exists(plan.RestartExecutable))
                return;

            var startInfo = new ProcessStartInfo(plan.RestartExecutable)
            {
                WorkingDirectory = plan.TargetDirectory,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(plan.RestartArguments))
                startInfo.Arguments = plan.RestartArguments;

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Restart failed: {ex.Message}{Environment.NewLine}");
        }
    }

    private static async Task TryRestartFromPlanAsync(string planPath, string logPath)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<AppUpdatePlan>(
                await File.ReadAllTextAsync(planPath, Encoding.UTF8),
                JsonOptions);
            if (plan != null)
                StartApplication(plan, logPath);
        }
        catch
        {
            // Если даже план не читается, просто оставляем лог.
        }
    }

    private static async Task TryMarkFailedAsync(string planPath, string error)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<AppUpdatePlan>(
                await File.ReadAllTextAsync(planPath, Encoding.UTF8),
                JsonOptions);
            if (plan != null)
                await UpdateHistoryAsync(plan, "Failed", null, error);
        }
        catch
        {
            // История не критична для восстановления файлов.
        }
    }

    private static async Task UpdateHistoryAsync(
        AppUpdatePlan plan,
        string status,
        DateTime? appliedAt,
        string error)
    {
        if (string.IsNullOrWhiteSpace(plan.HistoryFilePath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(plan.HistoryFilePath)!);
        var records = new List<AppUpdateRecord>();
        if (File.Exists(plan.HistoryFilePath))
        {
            records = JsonSerializer.Deserialize<List<AppUpdateRecord>>(
                await File.ReadAllTextAsync(plan.HistoryFilePath, Encoding.UTF8),
                JsonOptions) ?? new List<AppUpdateRecord>();
        }

        var record = records.FirstOrDefault(item =>
            item.UpdateId.Equals(plan.UpdateId, StringComparison.OrdinalIgnoreCase));
        if (record == null)
        {
            record = new AppUpdateRecord
            {
                UpdateId = plan.UpdateId,
                CreatedAt = DateTime.UtcNow
            };
            records.Add(record);
        }

        record.Version = plan.Version;
        record.Name = plan.Name;
        record.Description = plan.Description;
        record.Checksum = plan.Checksum;
        record.Status = status;
        record.Error = error;
        record.AppliedBy = Environment.UserName;
        record.AppliedAt = appliedAt ?? record.AppliedAt;

        await File.WriteAllTextAsync(
            plan.HistoryFilePath,
            JsonSerializer.Serialize(records.OrderByDescending(item => item.CreatedAt).ToList(), JsonOptions),
            Encoding.UTF8);
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string GetFullDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("В плане обновления указан пустой путь.");

        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(part => part == ".."))
        {
            throw new InvalidOperationException($"Недопустимый путь в обновлении: {value}");
        }

        return normalized;
    }

    private static string GetSafePath(string rootDirectory, string relativePath)
    {
        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Путь выходит за пределы папки: {relativePath}");

        return fullPath;
    }

    private static Task LogAsync(string logPath, string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        return File.AppendAllTextAsync(
            logPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
            Encoding.UTF8);
    }

    private sealed record CopiedFile(string TargetPath, string BackupPath, bool HadExistingFile);

    private sealed class AppUpdatePlan
    {
        public string UpdateId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public int MainProcessId { get; set; }
        public string SourceDirectory { get; set; } = string.Empty;
        public string TargetDirectory { get; set; } = string.Empty;
        public string BackupDirectory { get; set; } = string.Empty;
        public string RestartExecutable { get; set; } = string.Empty;
        public string RestartArguments { get; set; } = string.Empty;
        public string HistoryFilePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    private sealed class AppUpdateRecord
    {
        public string UpdateId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public DateTime? AppliedAt { get; set; }
        public string AppliedBy { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public string Error { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
