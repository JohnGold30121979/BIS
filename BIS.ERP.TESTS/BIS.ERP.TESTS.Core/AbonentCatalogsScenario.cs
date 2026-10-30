using System.Threading;
using BIS.ERP.Data;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Testing;

/// <summary>
/// Проверка справочников модуля «Абонентский отдел»: участки, льготы, улицы,
/// лицевые счета создаются при инициализации и привязываются к модулю.
/// </summary>
public sealed class AbonentCatalogsScenario : SmokeTestScenarioBase
{
    private static readonly string[] CatalogNames =
    {
        "Участки абонентского отдела",
        "Льготы",
        "Улицы",
        "Лицевые счета"
    };

    public override string Code => "abonent-catalogs";
    public override string Name => "Абонентский отдел: справочники";
    public override string Category => "Абонентский отдел";
    public override string Description =>
        "Создаёт и проверяет справочники участков, льгот, улиц и лицевых счетов.";
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var details = new List<string>();
        var errors = new List<string>();
        var candidates = await LoadDatabaseCandidatesAsync(LoadSettings(), progress, cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var context = new AppDbContext(candidate.ConnectionString);
            if (!await HasRequiredTableAsync(context, "MetadataObjects", cancellationToken))
            {
                details.Add($"{candidate.DatabaseName}: пропущена, это не информационная база BIS ERP.");
                continue;
            }

            Report(progress, $"Проверяется база: {candidate.DatabaseName}");

            try
            {
                await new RuntimeSchemaFixService(context).EnsureAsync();
                await new MetadataService(context).InitializePredefinedCatalogsAsync(Guid.Empty);
                await new ModuleMetadataService(context).EnsureAbonentModuleCatalogsAsync();

                foreach (var name in CatalogNames)
                {
                    var catalog = await context.MetadataObjects.AsNoTracking()
                        .Include(item => item.Fields)
                        .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == name,
                            cancellationToken);
                    if (catalog == null)
                    {
                        errors.Add($"{candidate.DatabaseName}: не создан справочник «{name}».");
                        continue;
                    }

                    var columns = string.Join(", ",
                        catalog.Fields.OrderBy(field => field.Order)
                            .Select(field => $"{field.Name} ({field.FieldType})"));
                    details.Add($"{candidate.DatabaseName}: «{name}» — {catalog.TableName}: {columns}");
                }

                var abonentCatalogIds = await context.MetadataModuleItems.AsNoTracking()
                    .Join(context.MetadataModules.AsNoTracking(),
                        item => item.ModuleId, module => module.Id, (item, module) => new { item.ObjectId, module.Code })
                    .Where(item => item.Code == ModuleMetadataService.AbonentModuleCode)
                    .Select(item => item.ObjectId)
                    .ToListAsync(cancellationToken);

                var assigned = await context.MetadataObjects.AsNoTracking()
                    .Where(item => abonentCatalogIds.Contains(item.Id) && CatalogNames.Contains(item.Name))
                    .Select(item => item.Name)
                    .ToListAsync(cancellationToken);

                details.Add($"{candidate.DatabaseName}: привязано к модулю — {string.Join(", ", assigned)}.");

                foreach (var missing in CatalogNames.Except(assigned, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{candidate.DatabaseName}: справочник «{missing}» не привязан к модулю «Абонентский отдел».");
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.DatabaseName}: {DescribeError(ex, candidate)}");
            }
        }

        return errors.Count == 0
            ? SmokeTestResult.Success("Справочники абонентского отдела созданы и привязаны.", details.ToArray())
            : SmokeTestResult.Failure("Справочники абонентского отдела: ошибки.", errors.Concat(details));
    }
}
