using BIS.ERP.Models;

namespace BIS.ERP.Services;

public static class ReportClassificationService
{
    private static readonly string[] ReconciliationMarkers =
    {
        "reconciliation",
        "akt_sver",
        "akt-sver",
        "aktsver",
        "sver",
        "свер"
    };

    public static bool IsReconciliationReport(Report report) =>
        string.Equals(report.ReportType, "ReconciliationAct", StringComparison.OrdinalIgnoreCase) ||
        HasReconciliationMarker(report.Name) ||
        HasReconciliationMarker(report.Code) ||
        HasReconciliationMarker(report.Description);

    public static bool HasReconciliationMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ReconciliationMarkers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
