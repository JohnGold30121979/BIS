using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public sealed class AppUpdatePackageService
    {
        public const string UpdateKind = "BIS.AppUpdate";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string LocalStateDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIS.ERP");

        private static string UpdatesDirectory => Path.Combine(LocalStateDirectory, "Updates");
        private static string BackupsDirectory => Path.Combine(LocalStateDirectory, "UpdateBackups");
        private static string DownloadsDirectory => Path.Combine(UpdatesDirectory, "Downloads");
        private static string HistoryFilePath => Path.Combine(LocalStateDirectory, "app-updates.json");

        public string CurrentAppVersion => GetCurrentAppVersion();

        public async Task<List<AppUpdateRecord>> GetHistoryAsync()
        {
            if (!File.Exists(HistoryFilePath))
                return new List<AppUpdateRecord>();

            await using var stream = File.OpenRead(HistoryFilePath);
            var records = await JsonSerializer.DeserializeAsync<List<AppUpdateRecord>>(stream, JsonOptions)
                ?? new List<AppUpdateRecord>();

            return records
                .OrderByDescending(record => record.AppliedAt ?? record.CreatedAt)
                .ThenByDescending(record => record.CreatedAt)
                .ToList();
        }

        public async Task<AppUpdateManifest> CreateManifestForFolderAsync(string sourceFolder)
        {
            if (!Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException(sourceFolder);

            var manifestPath = Path.Combine(sourceFolder, "manifest.json");
            if (File.Exists(manifestPath))
            {
                var manifest = JsonSerializer.Deserialize<AppUpdateManifest>(
                    await File.ReadAllTextAsync(manifestPath, Encoding.UTF8),
                    JsonOptions) ?? throw new InvalidOperationException("manifest.json поврежден.");
                ValidateManifest(manifest, allowEmptyFiles: true);
                return manifest;
            }

            return CreateDefaultManifest(sourceFolder, CurrentAppVersion);
        }

        public async Task CreateUpdateFromFolderAsync(
            string sourceFolder,
            string destinationFile,
            AppUpdateManifest? defaultManifest = null)
        {
            if (!Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException(sourceFolder);

            var manifestPath = Path.Combine(sourceFolder, "manifest.json");
            var manifest = defaultManifest ?? (File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<AppUpdateManifest>(
                      await File.ReadAllTextAsync(manifestPath, Encoding.UTF8),
                      JsonOptions)
                  ?? throw new InvalidOperationException("manifest.json поврежден.")
                : CreateDefaultManifest(sourceFolder, CurrentAppVersion));

            var destinationFullPath = Path.GetFullPath(destinationFile);
            manifest.Files = await BuildFileManifestAsync(sourceFolder, destinationFullPath, manifestPath);
            ValidateManifest(manifest, allowEmptyFiles: false);

            await using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using (var entryStream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(entryStream, manifest, JsonOptions);
                }

                foreach (var file in manifest.Files)
                {
                    var sourcePath = Path.Combine(sourceFolder, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    var entry = archive.CreateEntry($"files/{file.RelativePath}", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var sourceStream = File.OpenRead(sourcePath);
                    await sourceStream.CopyToAsync(entryStream);
                }
            }

            var encrypted = BisPackageCryptoService.Protect(memory.ToArray(), UpdateKind);
            await File.WriteAllBytesAsync(destinationFile, encrypted);
        }

        public async Task<AppUpdateInspectionResult> InspectUpdateAsync(string filePath)
        {
            var package = await ReadUpdatePackageAsync(filePath);
            return new AppUpdateInspectionResult
            {
                Manifest = package.Manifest,
                Checksum = package.Checksum,
                Entries = package.Entries
            };
        }

        public async Task<AppUpdateOnlineCheckResult> CheckOnlineUpdateAsync(
            string updateUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(updateUrl))
                throw new InvalidOperationException("В настройках не указана ссылка проверки обновлений.");
            if (!Uri.TryCreate(updateUrl.Trim(), UriKind.Absolute, out var sourceUri) ||
                sourceUri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException("Ссылка обновлений должна быть абсолютным HTTP/HTTPS URL.");
            }

            if (LooksLikePackageUrl(sourceUri))
            {
                var packagePath = await DownloadUpdatePackageAsync(sourceUri.ToString(), string.Empty, cancellationToken);
                var inspection = await InspectUpdateAsync(packagePath);
                return BuildOnlineResult(
                    sourceUri.ToString(),
                    sourceUri.ToString(),
                    inspection.Manifest.UpdateId,
                    inspection.Manifest.Version,
                    inspection.Manifest.Name,
                    inspection.Manifest.Description,
                    inspection.Checksum,
                    localPackagePath: packagePath,
                    isDirectPackage: true);
            }

            using var client = CreateHttpClient();
            var json = await client.GetStringAsync(sourceUri, cancellationToken);
            var feed = JsonSerializer.Deserialize<AppUpdateFeedManifest>(json, JsonOptions)
                ?? throw new InvalidOperationException("Фид обновлений поврежден.");
            ValidateFeed(feed);

            var packageUri = ResolvePackageUri(sourceUri, feed.PackageUrl);
            return BuildOnlineResult(
                sourceUri.ToString(),
                packageUri.ToString(),
                feed.UpdateId,
                feed.Version,
                feed.Name,
                feed.Description,
                feed.Checksum,
                localPackagePath: string.Empty,
                isDirectPackage: false);
        }

        public async Task<string> DownloadOnlineUpdatePackageAsync(
            AppUpdateOnlineCheckResult checkResult,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(checkResult.LocalPackagePath) &&
                File.Exists(checkResult.LocalPackagePath))
            {
                if (!string.IsNullOrWhiteSpace(checkResult.Checksum))
                    await ValidatePackageChecksumAsync(checkResult.LocalPackagePath, checkResult.Checksum);
                return checkResult.LocalPackagePath;
            }

            var packagePath = await DownloadUpdatePackageAsync(
                checkResult.PackageUrl,
                checkResult.Checksum,
                cancellationToken);
            checkResult.LocalPackagePath = packagePath;
            return packagePath;
        }

        public async Task<AppUpdateStageResult> StageUpdateAsync(string filePath)
        {
            var package = await ReadUpdatePackageAsync(filePath);
            ValidateManifest(package.Manifest, allowEmptyFiles: false);

            var safeId = NormalizeIdPart(package.Manifest.UpdateId);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var stagingDirectory = Path.Combine(UpdatesDirectory, $"{stamp}-{safeId}");
            var payloadDirectory = Path.Combine(stagingDirectory, "payload");
            var backupDirectory = Path.Combine(BackupsDirectory, $"{stamp}-{safeId}");
            Directory.CreateDirectory(payloadDirectory);
            Directory.CreateDirectory(backupDirectory);

            await ExtractPayloadAsync(package.PackageBytes, payloadDirectory, package.Manifest.Files);

            var updaterSource = Path.Combine(AppContext.BaseDirectory, "BIS.ERP.Updater.exe");
            if (!File.Exists(updaterSource))
            {
                throw new FileNotFoundException(
                    "Не найден BIS.ERP.Updater.exe. Соберите/опубликуйте приложение вместе с проектом обновления.",
                    updaterSource);
            }

            var updaterTarget = Path.Combine(stagingDirectory, "BIS.ERP.Updater.exe");
            File.Copy(updaterSource, updaterTarget, overwrite: true);

            var plan = new AppUpdatePlan
            {
                UpdateId = package.Manifest.UpdateId,
                Version = package.Manifest.Version,
                Name = package.Manifest.Name,
                Description = package.Manifest.Description,
                Checksum = package.Checksum,
                MainProcessId = Process.GetCurrentProcess().Id,
                SourceDirectory = payloadDirectory,
                TargetDirectory = AppContext.BaseDirectory,
                BackupDirectory = backupDirectory,
                RestartExecutable = Path.Combine(AppContext.BaseDirectory, package.Manifest.RestartExecutable),
                HistoryFilePath = HistoryFilePath,
                CreatedAt = DateTime.UtcNow
            };

            var planPath = Path.Combine(stagingDirectory, "update-plan.json");
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), Encoding.UTF8);
            await UpsertHistoryAsync(new AppUpdateRecord
            {
                UpdateId = package.Manifest.UpdateId,
                Version = package.Manifest.Version,
                Name = package.Manifest.Name,
                Description = package.Manifest.Description,
                Checksum = package.Checksum,
                AppliedBy = Environment.UserName,
                Status = "ReadyToApply",
                CreatedAt = DateTime.UtcNow
            });

            return new AppUpdateStageResult
            {
                Manifest = package.Manifest,
                Checksum = package.Checksum,
                StagingDirectory = stagingDirectory,
                PlanFilePath = planPath,
                UpdaterExecutablePath = updaterTarget
            };
        }

        public Process LaunchUpdater(AppUpdateStageResult stageResult)
        {
            if (!File.Exists(stageResult.UpdaterExecutablePath))
                throw new FileNotFoundException("Файл updater не найден.", stageResult.UpdaterExecutablePath);
            if (!File.Exists(stageResult.PlanFilePath))
                throw new FileNotFoundException("Файл плана обновления не найден.", stageResult.PlanFilePath);

            var startInfo = new ProcessStartInfo(stageResult.UpdaterExecutablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = stageResult.StagingDirectory
            };
            startInfo.ArgumentList.Add("--plan");
            startInfo.ArgumentList.Add(stageResult.PlanFilePath);

            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Не удалось запустить updater.");
        }

        public static AppUpdateManifest CreateDefaultManifest(string sourceFolder, string currentVersion)
        {
            var folderName = Path.GetFileName(Path.GetFullPath(sourceFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var safeName = string.IsNullOrWhiteSpace(folderName) ? "app-update" : folderName;
            var timestamp = DateTime.UtcNow;

            return new AppUpdateManifest
            {
                Format = UpdateKind,
                FormatVersion = 1,
                UpdateId = $"{timestamp:yyyy-MM-dd-HHmmss}-{NormalizeIdPart(safeName)}",
                Version = GetNextVersion(currentVersion),
                Name = $"Обновление программы {safeName}",
                Description = $"Автоматически сформированное обновление программы из папки {safeName}.",
                Author = Environment.UserName,
                MinAppVersion = currentVersion,
                RestartExecutable = "BIS.ERP.exe",
                CreatedAt = timestamp
            };
        }

        private AppUpdateOnlineCheckResult BuildOnlineResult(
            string sourceUrl,
            string packageUrl,
            string updateId,
            string latestVersion,
            string name,
            string description,
            string checksum,
            string localPackagePath,
            bool isDirectPackage)
        {
            var currentVersion = CurrentAppVersion;
            return new AppUpdateOnlineCheckResult
            {
                SourceUrl = sourceUrl,
                PackageUrl = packageUrl,
                UpdateId = updateId,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                Name = name,
                Description = description,
                Checksum = checksum,
                LocalPackagePath = localPackagePath,
                IsDirectPackage = isDirectPackage,
                IsUpdateAvailable = IsNewerVersion(latestVersion, currentVersion)
            };
        }

        private static async Task<string> DownloadUpdatePackageAsync(
            string packageUrl,
            string expectedChecksum,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out var packageUri) ||
                packageUri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException("URL пакета обновления должен быть абсолютным HTTP/HTTPS URL.");
            }

            Directory.CreateDirectory(DownloadsDirectory);
            using var client = CreateHttpClient();
            using var response = await client.GetAsync(
                packageUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var fileName = GetDownloadFileName(packageUri);
            var destinationPath = Path.Combine(
                DownloadsDirectory,
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{fileName}");

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(destinationPath))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(expectedChecksum))
                await ValidatePackageChecksumAsync(destinationPath, expectedChecksum);

            return destinationPath;
        }

        private static async Task ValidatePackageChecksumAsync(string filePath, string expectedChecksum)
        {
            var actualChecksum = BisPackageCryptoService.ComputeSha256(await File.ReadAllBytesAsync(filePath));
            if (!actualChecksum.Equals(expectedChecksum.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Контрольная сумма скачанного обновления не совпадает.");
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BIS.ERP-Updater/1.0");
            return client;
        }

        private static bool LooksLikePackageUrl(Uri uri) =>
            uri.AbsolutePath.EndsWith(".bisapp", StringComparison.OrdinalIgnoreCase);

        private static Uri ResolvePackageUri(Uri feedUri, string packageUrl)
        {
            if (string.IsNullOrWhiteSpace(packageUrl))
                throw new InvalidOperationException("В фиде обновлений не указан PackageUrl.");
            if (Uri.TryCreate(packageUrl.Trim(), UriKind.Absolute, out var absoluteUri))
                return absoluteUri;
            return new Uri(feedUri, packageUrl.Trim());
        }

        private static void ValidateFeed(AppUpdateFeedManifest feed)
        {
            if (!string.IsNullOrWhiteSpace(feed.Format) &&
                !feed.Format.Equals("BIS.AppUpdateFeed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Файл не является фидом обновлений BIS.");
            }

            if (string.IsNullOrWhiteSpace(feed.Version))
                throw new InvalidOperationException("В фиде обновлений не указана версия.");
            if (string.IsNullOrWhiteSpace(feed.PackageUrl))
                throw new InvalidOperationException("В фиде обновлений не указан PackageUrl.");
            if (string.IsNullOrWhiteSpace(feed.UpdateId))
                feed.UpdateId = $"online-{feed.Version.Trim()}";
            if (string.IsNullOrWhiteSpace(feed.Name))
                feed.Name = $"Обновление программы {feed.Version.Trim()}";
        }

        private static string GetDownloadFileName(Uri uri)
        {
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "BIS.ERP.update.bisapp";
            if (!fileName.EndsWith(".bisapp", StringComparison.OrdinalIgnoreCase))
                fileName += ".bisapp";

            foreach (var invalid in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalid, '-');

            return fileName;
        }

        private static bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            var latest = ParseVersionParts(latestVersion);
            var current = ParseVersionParts(currentVersion);
            var count = Math.Max(latest.Count, current.Count);

            for (var index = 0; index < count; index++)
            {
                var latestPart = index < latest.Count ? latest[index] : 0;
                var currentPart = index < current.Count ? current[index] : 0;
                if (latestPart > currentPart)
                    return true;
                if (latestPart < currentPart)
                    return false;
            }

            return false;
        }

        private static List<int> ParseVersionParts(string version)
        {
            var clean = (version ?? string.Empty)
                .Split('+')[0]
                .Split('-')[0]
                .Trim();

            return clean
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.TryParse(part, out var value) ? value : 0)
                .ToList();
        }

        private static async Task<List<AppUpdateFile>> BuildFileManifestAsync(
            string sourceFolder,
            string destinationFullPath,
            string manifestPath)
        {
            var files = new List<AppUpdateFile>();
            foreach (var path in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(path);
                if (fullPath.Equals(destinationFullPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fullPath.Equals(Path.GetFullPath(manifestPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                var relativePath = Path.GetRelativePath(sourceFolder, fullPath).Replace('\\', '/');
                if (relativePath.StartsWith(".", StringComparison.Ordinal) ||
                    relativePath.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.Contains("/.vs/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = new FileInfo(fullPath);
                files.Add(new AppUpdateFile
                {
                    RelativePath = NormalizeRelativePath(relativePath),
                    Length = info.Length,
                    Sha256 = await ComputeFileHashAsync(fullPath)
                });
            }

            return files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static async Task ExtractPayloadAsync(
            byte[] packageBytes,
            string payloadDirectory,
            List<AppUpdateFile> expectedFiles)
        {
            var expected = expectedFiles.ToDictionary(
                file => NormalizeRelativePath(file.RelativePath),
                StringComparer.OrdinalIgnoreCase);

            await using var memory = new MemoryStream(packageBytes);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith("files/", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var relativePath = NormalizeRelativePath(entry.FullName["files/".Length..]);
                if (!expected.TryGetValue(relativePath, out var expectedFile))
                    throw new InvalidOperationException($"Файл {relativePath} отсутствует в манифесте обновления.");

                var destinationPath = GetSafePath(payloadDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                await using (var source = entry.Open())
                await using (var target = File.Create(destinationPath))
                {
                    await source.CopyToAsync(target);
                }

                var actualHash = await ComputeFileHashAsync(destinationPath);
                if (!actualHash.Equals(expectedFile.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Контрольная сумма файла {relativePath} не совпадает.");
            }

            var extracted = Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(payloadDirectory, path)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var expectedFile in expected.Keys)
            {
                if (!extracted.Contains(expectedFile))
                    throw new InvalidOperationException($"В пакете не найден файл {expectedFile}.");
            }
        }

        private async Task<UpdatePackageContent> ReadUpdatePackageAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл обновления не найден.", filePath);

            var encryptedBytes = await File.ReadAllBytesAsync(filePath);
            var packageBytes = BisPackageCryptoService.Unprotect(encryptedBytes, UpdateKind);
            var checksum = BisPackageCryptoService.ComputeSha256(encryptedBytes);

            await using var memory = new MemoryStream(packageBytes);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
            var manifestEntry = archive.GetEntry("manifest.json")
                ?? throw new InvalidOperationException("В обновлении отсутствует manifest.json.");

            string manifestText;
            await using (var stream = manifestEntry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                manifestText = await reader.ReadToEndAsync();
            }

            var manifest = JsonSerializer.Deserialize<AppUpdateManifest>(manifestText, JsonOptions)
                ?? throw new InvalidOperationException("manifest.json поврежден.");
            ValidateManifest(manifest, allowEmptyFiles: false);

            return new UpdatePackageContent
            {
                Manifest = manifest,
                Checksum = checksum,
                PackageBytes = packageBytes,
                Entries = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToList()
            };
        }

        private async Task UpsertHistoryAsync(AppUpdateRecord record)
        {
            Directory.CreateDirectory(LocalStateDirectory);
            var records = await GetHistoryAsync();
            var existingIndex = records.FindIndex(item =>
                item.UpdateId.Equals(record.UpdateId, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
                records[existingIndex] = record;
            else
                records.Add(record);

            await File.WriteAllTextAsync(
                HistoryFilePath,
                JsonSerializer.Serialize(records.OrderByDescending(item => item.CreatedAt).ToList(), JsonOptions),
                Encoding.UTF8);
        }

        private static void ValidateManifest(AppUpdateManifest manifest, bool allowEmptyFiles)
        {
            if (!UpdateKind.Equals(manifest.Format, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Файл не является обновлением программы BIS.");
            if (manifest.FormatVersion != 1)
                throw new InvalidOperationException($"Версия формата обновления {manifest.FormatVersion} не поддерживается.");
            if (string.IsNullOrWhiteSpace(manifest.UpdateId))
                throw new InvalidOperationException("В обновлении не указан UpdateId.");
            if (string.IsNullOrWhiteSpace(manifest.Version))
                throw new InvalidOperationException("В обновлении не указана версия.");
            if (string.IsNullOrWhiteSpace(manifest.Name))
                throw new InvalidOperationException("В обновлении не указано наименование.");
            if (string.IsNullOrWhiteSpace(manifest.RestartExecutable))
                throw new InvalidOperationException("В обновлении не указан исполняемый файл для запуска.");
            if (!allowEmptyFiles && manifest.Files.Count == 0)
                throw new InvalidOperationException("В обновлении нет файлов.");

            manifest.UpdateId = manifest.UpdateId.Trim();
            manifest.Version = manifest.Version.Trim();
            manifest.Name = manifest.Name.Trim();
            manifest.RestartExecutable = NormalizeRelativePath(manifest.RestartExecutable);
        }

        private static string GetCurrentAppVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AppUpdatePackageService).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+')[0];

            return assembly.GetName().Version?.ToString() ?? "1.0.0";
        }

        private static string GetNextVersion(string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(currentVersion))
                return "1.0.1";

            var cleanVersion = currentVersion.Split('+')[0].Split('-')[0];
            var parts = cleanVersion.Split('.');
            if (parts.Length >= 3 &&
                int.TryParse(parts[0], out var major) &&
                int.TryParse(parts[1], out var minor) &&
                int.TryParse(parts[2], out var patch))
            {
                return $"{major}.{minor}.{patch + 1}";
            }

            if (parts.Length == 2 &&
                int.TryParse(parts[0], out major) &&
                int.TryParse(parts[1], out minor))
            {
                return $"{major}.{minor}.1";
            }

            return $"{currentVersion.Trim()}.1";
        }

        private static string NormalizeIdPart(string value)
        {
            var builder = new StringBuilder();
            foreach (var ch in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
                else if (ch is '-' or '_' or ' ')
                    builder.Append('-');
            }

            var result = builder.ToString().Trim('-');
            while (result.Contains("--", StringComparison.Ordinal))
                result = result.Replace("--", "-", StringComparison.Ordinal);

            return string.IsNullOrWhiteSpace(result) ? "app-update" : result;
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
                throw new InvalidOperationException($"Путь выходит за пределы папки обновления: {relativePath}");

            return fullPath;
        }

        private static async Task<string> ComputeFileHashAsync(string path)
        {
            await using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private sealed class UpdatePackageContent
        {
            public AppUpdateManifest Manifest { get; set; } = new();
            public string Checksum { get; set; } = string.Empty;
            public byte[] PackageBytes { get; set; } = Array.Empty<byte>();
            public List<string> Entries { get; set; } = new();
        }
    }
}
