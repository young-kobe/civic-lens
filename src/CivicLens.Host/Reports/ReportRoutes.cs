namespace CivicLens.Host.Reports;

internal static class ReportRoutes
{
    public const string List = "/Review/Reports";
    public const string ResolvedFilter = "resolved";
    public const string DismissedStatus = "reportDismissed";

    public static string Detail(Guid reportId) => $"{List}/{reportId:D}";

    public static string WithDismissedStatus(string path) => $"{path}?status={DismissedStatus}";
}
