using BIS.ERP.Data;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Testing;

/// <summary>
/// Проверяет изоляцию окон по подключениям: у каждого окна/диалога свой
/// <see cref="AppDbContext"/>, поэтому одновременные загрузки разных окон
/// не падают с «A second operation was started on this context instance…»
/// или «A command is already in progress…».
///
/// Сценарий работает только с базой из testsettings.json и ничего не пишет:
/// несколько независимых сервисов параллельно читают метаданные и справочники.
/// </summary>
public sealed class ContextIsolationScenario : SmokeTestScenarioBase
{
    private const int ParallelWindows = 4;
    private const int Iterations = 10;

    public override string Code => "concurrency-isolation";
    public override string Name => "Конкуренция: изоляция окон по подключениям";
    public override string Category => "Конкуренция";
    public override string Description =>
        "Параллельные чтения из нескольких независимых контекстов (как из разных вкладок MDI) не должны давать ошибок контекста.";
    public override bool SupportsRun => false;
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var details = new List<string>();
        var errors = new List<string>();

        var settings = LoadSettings();
        var candidate = new DatabaseCandidate(
            "testsettings",
            settings.DatabaseName,
            settings.ConnectionString(settings.DatabaseName));

        Report(progress, $"Проверяется база: {candidate.DatabaseName}");

        await using var probe = new AppDbContext(candidate.ConnectionString);
        if (!await HasRequiredTableAsync(probe, "MetadataObjects", cancellationToken))
        {
            return SmokeTestResult.Failure(
                $"{candidate.DatabaseName}: это не информационная база BIS ERP — нет MetadataObjects.");
        }

        // Корневой сервис — как MainWorkWindow. Каждое «окно» получает СВОЁ
        // подключение через боевой путь MetadataService.CreateIndependent().
        var root = new MetadataService(probe);
        var windowServices = new List<MetadataService>();
        for (var index = 0; index < ParallelWindows; index++)
            windowServices.Add(root.CreateIndependent());

        var catalogs = await windowServices[0].GetCatalogsAsync();
        if (catalogs.Count == 0)
            errors.Add("В базе нет справочников — нечего читать.");

        var catalogId = catalogs.Count > 0 ? catalogs[0].Id : Guid.Empty;

        for (var iteration = 1; iteration <= Iterations && errors.Count == 0; iteration++)
        {
            try
            {
                // Все окна одновременно грузят метаданные и данные справочника.
                await Task.WhenAll(windowServices.Select(service => service.GetCatalogsAsync()));
                if (catalogId != Guid.Empty)
                {
                    await Task.WhenAll(windowServices.Select(service =>
                        service.GetCatalogDataAsync(catalogId)));
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Итерация {iteration}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        details.Add($"Окон: {ParallelWindows}, итераций: {Iterations}, справочников: {catalogs.Count}.");

        return errors.Count == 0
            ? SmokeTestResult.Success("Параллельные чтения из независимых подключений прошли без ошибок.", details.ToArray())
            : SmokeTestResult.Failure("Параллельные чтения из независимых подключений дали ошибки.", errors.Concat(details));
    }
}
