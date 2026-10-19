namespace CivicLens.Host.Review;

internal static class VoteRoutes
{
    public const string List = "/Review/Votes";
    public const string Triage = List + "/Triage";

    public static string Editor(string recordId, string? status = null) =>
        $"{List}/{recordId}" + (status is null ? "" : "?status=" + status);
}
