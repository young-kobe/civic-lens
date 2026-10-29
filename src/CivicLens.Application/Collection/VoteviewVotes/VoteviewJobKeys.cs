using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.VoteviewVotes;

public static class VoteviewJobKeys
{
    private const string Prefix = "voteview-senate/";
    private const string Final = "final";

    public static string File(string sourceId, ApiResource resource, int congress, DateOnly today) =>
        $"{Prefix}{sourceId}/{resource.ToString().ToLowerInvariant()}/{congress}/{SweepDay(congress, today)}";

    private static string SweepDay(int congress, DateOnly today) =>
        congress == VoteSession.ForYear(today.Year).Congress ? RenderedText.Date(today) : Final;
}
