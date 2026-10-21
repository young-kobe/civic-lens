namespace CivicLens.Application.Collection.SenateVotes;

public static class SenateVoteJobKeys
{
    private const string Prefix = "senate-vote/";
    private const int RefetchDays = 30;

    public static string Vote(string sourceId, SenateVoteMenuItem item, DateOnly today) =>
        $"{Prefix}{sourceId}/vote/{item.Roll.Session.Congress}/{item.Roll.Session.Number}/{item.Roll.Number}/{FetchRound(item.Day, today)}";

    public static string NextMenu(string sourceId, string menuJobId) => $"{Prefix}{sourceId}/menu/{menuJobId}";

    private static string FetchRound(DateOnly voteDay, DateOnly today) =>
        voteDay >= today.AddDays(-RefetchDays) ? RenderedText.Date(today) : "final";
}
