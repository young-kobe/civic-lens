namespace CivicLens.Host.Briefs;

internal static class BriefRoutes
{
    public const string List = "/Review/Briefs";

    public static string Editor(string recordId, string? status = null) =>
        $"{List}/{recordId}" + (status is null ? "" : "?status=" + status);
}
