namespace CivicLens.Host.Review;

internal static class ReportRoutes
{
    public const string List = "/Review/Reports";
    public const string ResolvedFilter = "resolved";

    public static string Detail(Guid reportId, string? status = null) =>
        $"{List}/{reportId:D}" + (status is null ? "" : "?status=" + status);
}
