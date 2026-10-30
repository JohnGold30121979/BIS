using System.Reflection;
using System.Globalization;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using BIS.ERP.Data;
using BIS.ERP.Services;
using BIS.ERP.Views;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

/// <summary>Воспроизводит устаревший кэш ссылок без добавления или изменения данных базы.</summary>
public sealed class AccountPairReferenceRefreshScenario : SmokeTestScenarioBase
{
    public override string Code => "account-pair-reference-refresh";
    public override string Name => "Выбор пары: счета вместо GUID после добавления";
    public override string Category => "Интерфейс";
    public override string Description => "Исключает счета выбранной пары из исходного кэша, перезагружает настоящее окно выбора и проверяет расшифровку Дт/Кт и выделение строки. База только для чтения.";
    public override bool SupportsRun => false;
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(SmokeTestRunOptions options,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var candidates = await LoadDatabaseCandidatesAsync(LoadSettings(), progress, cancellationToken);
        var target = candidates.FirstOrDefault(item => item.DatabaseName.StartsWith("bis_OsOO2_", StringComparison.OrdinalIgnoreCase));
        if (target == null) return SmokeTestResult.Failure("Не найдена ОСОО2 для проверки.");
        var completion = new TaskCompletionSource<SmokeTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                ReferenceSelectionDialog? dialog = null;
                try
                {
                    var connection = new NpgsqlConnectionStringBuilder(target.ConnectionString)
                        { Options = "-c default_transaction_read_only=on" };
                    await using var context = new AppDbContext(connection.ToString());
                    var metadata = new MetadataService(context);
                    var catalog = await context.MetadataObjects.Include(item => item.Fields)
                        .SingleAsync(item => item.TableName == "catalog_advance_payments", cancellationToken);
                    var rows = await metadata.GetCatalogDataAsync(catalog.Id);
                    var maps = await ReferenceDisplayHelper.LoadMapsAsync(catalog, metadata);
                    var debit = catalog.Fields.Single(field => field.DbColumnName == "debit_account").Name;
                    var credit = catalog.Fields.Single(field => field.DbColumnName == "credit_account").Name;
                    var code = catalog.Fields.Single(field => field.DbColumnName == "code").Name;
                    var name = catalog.Fields.Single(field => field.DbColumnName == "name").Name;
                    bool HasAccount(Dictionary<string, object> row, string field) =>
                        Guid.TryParse(row.GetValueOrDefault(field)?.ToString(), out var id) && maps[field].ContainsKey(id);
                    var selected = rows.Where(row => HasAccount(row, debit) && HasAccount(row, credit))
                        .OrderByDescending(row => row.GetValueOrDefault(code)?.ToString() == "12").First();
                    var selectedId = Guid.Parse(selected["Id"].ToString()!);
                    var debitId = Guid.Parse(selected[debit].ToString()!);
                    var creditId = Guid.Parse(selected[credit].ToString()!);
                    var expectedDebit = maps[debit][debitId];
                    var expectedCredit = maps[credit][creditId];
                    var stale = maps.ToDictionary(item => item.Key, item => new Dictionary<Guid, string>(item.Value));
                    stale[debit].Remove(debitId);
                    stale[credit].Remove(creditId);
                    dialog = new ReferenceSelectionDialog(rows.Where(row => !Equals(row["Id"], selected["Id"])).ToList(), code, name, stale);
                    dialog.ConfigureCatalogEditing(catalog, metadata);
                    var reload = typeof(ReferenceSelectionDialog).GetMethod("ReloadItemsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    await (Task)reload.Invoke(dialog, new object?[] { selectedId })!;
                    var grid = (DataGrid)dialog.FindName("ItemsGrid");
                    var reloaded = grid.Items.Cast<Dictionary<string, object>>().Single(row => Guid.Parse(row["Id"].ToString()!) == selectedId);
                    if (reloaded[debit]?.ToString() != expectedDebit || reloaded[credit]?.ToString() != expectedCredit)
                        throw new Exception("После перезагрузки Дт/Кт не расшифрованы свежими картами ссылок.");
                    if (!ReferenceEquals(grid.SelectedItem, reloaded)) throw new Exception("Выделение добавленной строки потеряно.");
                    if (Guid.TryParse(reloaded[debit]?.ToString(), out _) || Guid.TryParse(reloaded[credit]?.ToString(), out _))
                        throw new Exception("Вместо названия счёта отображается GUID.");
                    var module = catalog.Fields.Single(field => field.DbColumnName == "module_code").Name;
                    var moduleColumn = grid.Columns.OfType<DataGridTextColumn>().Single(column => column.Header?.ToString() == module);
                    var binding = (Binding)moduleColumn.Binding;
                    foreach (var value in new[] { "3", "Finance", "Финансы" })
                        if (binding.Converter.Convert(value, typeof(string), binding.ConverterParameter, CultureInfo.InvariantCulture)?.ToString() != "Финансы")
                            throw new Exception($"Модуль «{value}» не отображается как «Финансы».");
                    completion.TrySetResult(SmokeTestResult.Success("Новые счета отображаются без GUID после обновления выбора.",
                        $"ОСОО2, пара {selected[code]}; Дт: {expectedDebit}; Кт: {expectedCredit}.",
                        "Проверено настоящее окно и ReloadItemsAsync с заведомо устаревшим кэшем; строка выделена; база read-only.",
                        "Поле остатка: 3 / Finance / Финансы отображаются как Финансы, без изменения сохранённого значения."));
                }
                catch (Exception ex) { completion.TrySetResult(SmokeTestResult.Failure("Обновление ссылок: ошибка.", new[] { DescribeError(ex) })); }
                finally { dialog?.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);
    }
}
