namespace CivicLens.Host.Briefs;

internal static class BriefRoutes
{
    private const string Base = "/Review/Briefs";

    public static string Editor(string recordId, string? status = null) =>
        $"{Base}/{recordId}" + (status is null ? "" : "?status=" + status);
}
