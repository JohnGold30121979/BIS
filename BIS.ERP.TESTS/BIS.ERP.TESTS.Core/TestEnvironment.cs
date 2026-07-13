using System.Text;
using System.Text.Json;

namespace BIS.ERP.Testing;

public static class TestEnvironment
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string ResolveTestsRootDirectory()
    {
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in roots
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = new DirectoryInfo(root);
            while (directory != null)
            {
                if (string.Equals(directory.Name, "BIS.ERP.TESTS", StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(Path.Combine(directory.FullName, "BIS.ERP.TESTS.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return Path.GetFullPath(Directory.GetCurrentDirectory());
    }

    public static string GetConfigDirectory()
    {
        var directory = Path.Combine(ResolveTestsRootDirectory(), "config");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetLogsDirectory()
    {
        var directory = Path.Combine(ResolveTestsRootDirectory(), "logs");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetSettingsFilePath()
    {
        return Path.Combine(GetConfigDirectory(), "testsettings.json");
    }

    public static string FormatLogEntry(string message)
    {
        return $"[{DateTime.Now:HH:mm:ss}] {message}";
    }

    public static TestSettings LoadSettings()
    {
        var paths = new[]
        {
            GetSettingsFilePath(),
            Path.Combine(ResolveTestsRootDirectory(), "testsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "testsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "testsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.json")
        };

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                continue;

            try
            {
                var settings = JsonSerializer.Deserialize<TestSettings>(File.ReadAllText(path), JsonOptions);
                if (settings != null)
                    return settings;
            }
            catch
            {
                // Не валим тестовый контур из-за поврежденного файла конфигурации.
            }
        }

        return new TestSettings();
    }

    public static void SaveSettings(TestSettings settings)
    {
        var path = GetSettingsFilePath();
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
    }
}

public sealed class TestLogWriter
{
    private readonly object _syncRoot = new();

    private TestLogWriter(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static TestLogWriter Create(string sourceName)
    {
        var safeName = SanitizeFileName(sourceName);
        var fileName = $"{safeName}-{DateTime.Now:yyyyMMdd}.log";
        return new TestLogWriter(Path.Combine(TestEnvironment.GetLogsDirectory(), fileName));
    }

    public void WriteLine(string message)
    {
        try
        {
            lock (_syncRoot)
            {
                File.AppendAllText(FilePath, message + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Отсутствие лог-файла не должно ронять тесты.
        }
    }

    private static string SanitizeFileName(string sourceName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(sourceName.Length);

        foreach (var character in sourceName)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        return builder.Length == 0 ? "tests" : builder.ToString();
    }
}

public sealed record ConnectionCheckResult(
    bool IsSuccess,
    string Message,
    string? DatabaseName = null,
    string? Username = null);
