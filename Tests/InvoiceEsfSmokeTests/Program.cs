using BIS.ERP.Testing;

var command = ParseCommand(args);
var scenario = SmokeTestRegistry.FindByCode("invoice-esf")
    ?? throw new InvalidOperationException("Сценарий invoice-esf не найден.");

var progress = new Progress<string>(message => Console.WriteLine(message));
var result = await scenario.ExecuteAsync(command, progress);

if (result.IsSuccess)
{
    Console.WriteLine(result.Summary);
    foreach (var detail in result.Details)
        Console.WriteLine(detail);
    return 0;
}

Console.Error.WriteLine(result.Summary);
foreach (var detail in result.Details)
    Console.Error.WriteLine($"- {detail}");
return 1;

static SmokeTestCommand ParseCommand(string[] arguments)
{
    if (arguments.Length == 0)
        return SmokeTestCommand.Verify;

    return arguments[0].Trim().ToLowerInvariant() switch
    {
        "run" => SmokeTestCommand.Run,
        "cleanup" => SmokeTestCommand.Cleanup,
        "clean" => SmokeTestCommand.Cleanup,
        "delete" => SmokeTestCommand.Cleanup,
        "verify" => SmokeTestCommand.Verify,
        "check" => SmokeTestCommand.Verify,
        _ => throw new InvalidOperationException("Неизвестный режим. Используйте: run, cleanup или verify.")
    };
}
