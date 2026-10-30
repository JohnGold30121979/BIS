using System.Data;
using System.Windows;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Views;

public partial class AccountingReportsView
{
    private TurnoverAccountPair? _selectedTurnoverPair;

    private async void OnSelectTurnoverPairClick(object sender, RoutedEventArgs e)
    {
        try
        {
            IsEnabled = false;
            var catalog = await _context.MetadataObjects.Include(item => item.Fields)
                .SingleOrDefaultAsync(item => item.ObjectType == "Catalog" && item.TableName == "catalog_advance_payments")
                ?? throw new InvalidOperationException("Не найден справочник «Пары счетов».");
            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var maps = await ReferenceDisplayHelper.LoadMapsAsync(catalog, _metadataService);
            var codeField = catalog.Fields.FirstOrDefault(field => field.DbColumnName == "code")?.Name ?? "Код";
            var nameField = catalog.Fields.FirstOrDefault(field => field.DbColumnName == "name")?.Name ?? "Вид расчета";
            var dialog = new ReferenceSelectionDialog(rows, codeField, nameField, maps) { Title = "Выбор: Пары счетов" };
            dialog.ConfigureCatalogEditing(catalog, _metadataService);
            if (await MdiDialogService.ShowInWorkspaceForResultAsync(Window.GetWindow(this), dialog, dialog.Title) != true
                || dialog.SelectedItem == null) return;
            // В окне выбора пару могли только что создать или изменить: перечитываем её.
            var pairs = await AccountPairTurnoverBuilder.LoadPairsAsync(_metadataService);
            if (TryReadGuid(dialog.SelectedItem, out var id, "Id"))
                _selectedTurnoverPair = pairs.FirstOrDefault(pair => pair.Id == id)
                    ?? throw new InvalidOperationException("Выбранная пара неактивна или в ней не заполнены оба счёта из плана счетов.");
            TurnoverPairTextBox.Text = _selectedTurnoverPair == null ? "" :
                $"{_selectedTurnoverPair.FirstAccount} - {_selectedTurnoverPair.SecondAccount} ({_selectedTurnoverPair.Name})";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка выбора пары счетов: {ex.Message}", "Пары счетов", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async Task<(DataTable, Report)> BuildAccountPairTurnoverAsync(DateTime start, DateTime end)
    {
        if (_selectedTurnoverPair == null)
            throw new InvalidOperationException("Выберите пару счетов кнопкой «?».");
        var pairs = await AccountPairTurnoverBuilder.LoadPairsAsync(_metadataService);
        var pair = pairs.FirstOrDefault(item => item.Id == _selectedTurnoverPair.Id)
            ?? throw new InvalidOperationException("Выбранная пара удалена, неактивна или содержит недоступные счета. Выберите пару заново.");
        var all = AllOrganizationsCheckBox.IsChecked == true;
        if (!all && (!_selectedOrganizationId.HasValue || _selectedOrganizationId == Guid.Empty))
            throw new InvalidOperationException("Выберите организацию или установите «Все организации».");
        var data = await AccountPairTurnoverBuilder.LoadAsync(_context, pair, all ? null : _selectedOrganizationId, start, end);
        var stored = await _context.Reports.AsNoTracking()
            .SingleOrDefaultAsync(report => report.Code == AccountPairTurnoverBuilder.ReportCode)
            ?? throw new InvalidOperationException("Не найден макет ведомости по паре счетов. Повторно откройте информационную базу.");
        var report = CreateReport(data, AccountPairTurnoverBuilder.ReportName, true);
        report.ReportType = AccountPairTurnoverBuilder.ReportType;
        report.Code = AccountPairTurnoverBuilder.ReportCode;
        report.SourceFormat = "ClosedXml";
        report.Template = string.Empty;
        report.ClosedXmlLayout = stored.ClosedXmlLayout;
        report.PageOrientation = "Portrait";
        report.ShowGrandTotal = false;
        report.AlternateRowColors = false;
        return (data, report);
    }
}
