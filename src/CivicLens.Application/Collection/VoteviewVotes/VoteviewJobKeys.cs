using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.VoteviewVotes;

public static class VoteviewJobKeys
{
    private const string Prefix = "voteview-senate/";
    private const string Final = "final";

    public static string RollCalls(string sourceId, int congress, DateOnly today) =>
        $"{Prefix}{sourceId}/rollcalls/{congress}/{SweepDay(congress, today)}";

    public static string Members(string sourceId, string rollCallsJobId) => $"{Prefix}{sourceId}/members/{rollCallsJobId}";

    public static string Votes(string sourceId, string membersJobId) => $"{Prefix}{sourceId}/votes/{membersJobId}";

    public static string ParentJobId(string key) => key[(key.LastIndexOf('/') + 1)..];

    private static string SweepDay(int congress, DateOnly today) =>
        congress == CurrentCongress(today) ? RenderedText.Date(today) : Final;

    public static int CurrentCongress(DateOnly today) => VoteSession.ForYear(today.Year).Congress;
}
