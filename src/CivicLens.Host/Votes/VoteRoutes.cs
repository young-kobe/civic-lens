namespace CivicLens.Host.Votes;

internal static class VoteRoutes
{
    public static string Editor(string recordId, string? status = null) =>
        $"/Review/Votes/{recordId}" + (status is null ? "" : "?status=" + status);
}
