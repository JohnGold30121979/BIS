using BIS.ERP.Models;

namespace BIS.ERP.Services;

public static class ReportClassificationService
{
    private const string StandardReconciliationCodePrefix = "standard.frx.finance.reconciliation.";

    private static readonly string[] ReconciliationMarkers =
    {
        "reconciliation",
        "akt_sver",
        "akt-sver",
        "aktsver",
        "sver",
        "свер"
    };

    public static bool IsFoxProReportTemplate(Report report) =>
        string.Equals(report.SourceFormat, "FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(report.ReportType, "FoxProLayout", StringComparison.OrdinalIgnoreCase);

    public static bool IsStandardReconciliationReport(Report report) =>
        !string.IsNullOrWhiteSpace(report.Code) &&
        report.Code.StartsWith(StandardReconciliationCodePrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsReconciliationReport(Report report) =>
        string.Equals(report.ReportType, "ReconciliationAct", StringComparison.OrdinalIgnoreCase) ||
        IsStandardReconciliationReport(report) ||
        HasReconciliationMarker(report.Name) ||
        HasReconciliationMarker(report.Code) ||
        HasReconciliationMarker(report.Description) ||
        (IsFoxProReportTemplate(report) && HasReconciliationMarker(report.Template));

    public static bool HasReconciliationMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ReconciliationMarkers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
