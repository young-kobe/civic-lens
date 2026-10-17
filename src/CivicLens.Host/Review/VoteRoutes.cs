namespace CivicLens.Host.Review;

internal static class VoteRoutes
{
    public const string List = "/Review/Votes";

    public static string Editor(string recordId, string? status = null) =>
        $"{List}/{recordId}" + (status is null ? "" : "?status=" + status);
}
