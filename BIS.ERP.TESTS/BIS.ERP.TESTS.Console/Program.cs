using BIS.ERP.Testing;

var logWriter = TestLogWriter.Create("console");
var parsed = await ParseOptionsAsync(args);
var scenario = SmokeTestRegistry.FindByCode("invoice-esf")
    ?? throw new InvalidOperationException("Сценарий invoice-esf не найден.");

LogLine($"Запуск сценария {scenario.Code} в режиме {parsed.Command}.", logWriter);
LogLine($"Файл настроек: {TestEnvironment.GetSettingsFilePath()}", logWriter);
LogLine($"Папка логов: {TestEnvironment.GetLogsDirectory()}", logWriter);

var progress = new Progress<string>(message => LogLine(message, logWriter));
var result = await scenario.ExecuteAsync(parsed, progress);

if (result.IsSuccess)
{
    LogLine(result.Summary, logWriter);
    foreach (var detail in result.Details)
        LogLine(detail, logWriter);
    return 0;
}

LogError(result.Summary, logWriter);
foreach (var detail in result.Details)
    LogError($"- {detail}", logWriter);
return 1;

static async Task<SmokeTestRunOptions> ParseOptionsAsync(string[] arguments)
{
    var command = SmokeTestCommand.Verify;
    var cycleCount = 1;
    var documentCount = 1;
    string? filePath = null;

    for (var index = 0; index < arguments.Length; index++)
    {
        var argument = arguments[index].Trim();
        switch (argument.ToLowerInvariant())
        {
            case "run":
                command = SmokeTestCommand.Run;
                break;
            case "cleanup":
            case "clean":
            case "delete":
                command = SmokeTestCommand.Cleanup;
                break;
            case "verify":
            case "check":
                command = SmokeTestCommand.Verify;
                break;
            case "--cycles":
            case "-c":
                cycleCount = ReadInt(arguments, ref index, 1, "cycles");
                break;
            case "--documents":
            case "-d":
                documentCount = ReadInt(arguments, ref index, 1, "documents");
                break;
            case "--file":
            case "-f":
                filePath = ReadString(arguments, ref index, "file");
                break;
            default:
                throw new InvalidOperationException(
                    $"Неизвестный параметр '{argument}'. Используйте run|verify|cleanup, --cycles, --documents, --file.");
        }
    }

    IReadOnlyCollection<SmokeTestOperation> operations = Array.Empty<SmokeTestOperation>();
    if (!string.IsNullOrWhiteSpace(filePath))
    {
        var loader = new OperationImportService();
        operations = await loader.LoadAsync(filePath);
    }

    return new SmokeTestRunOptions
    {
        Command = command,
        CycleCount = Math.Max(1, cycleCount),
        DocumentCount = Math.Max(1, documentCount),
        OperationsFilePath = filePath,
        Operations = operations
    };
}

static int ReadInt(string[] arguments, ref int index, int fallback, string optionName)
{
    if (index + 1 >= arguments.Length)
        return fallback;

    index++;
    if (int.TryParse(arguments[index], out var value))
        return value;

    throw new InvalidOperationException($"Параметр {optionName} должен быть целым числом.");
}

static string ReadString(string[] arguments, ref int index, string optionName)
{
    if (index + 1 >= arguments.Length)
        throw new InvalidOperationException($"Для параметра {optionName} не указано значение.");

    index++;
    return arguments[index];
}

static void LogLine(string message, TestLogWriter logWriter)
{
    var entry = TestEnvironment.FormatLogEntry(message);
    Console.WriteLine(entry);
    logWriter.WriteLine(entry);
}

static void LogError(string message, TestLogWriter logWriter)
{
    var entry = TestEnvironment.FormatLogEntry(message);
    Console.Error.WriteLine(entry);
    logWriter.WriteLine(entry);
}
