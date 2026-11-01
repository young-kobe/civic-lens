using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.VoteviewVotes;

public static class VoteviewJobKeys
{
    private const string Prefix = "voteview-senate/";
    public static string RollCalls(string sourceId, string parentRollCallsJobId) => $"{Prefix}{sourceId}/rollcalls/{parentRollCallsJobId}";

    public static string Members(string sourceId, string rollCallsJobId) => $"{Prefix}{sourceId}/members/{rollCallsJobId}";

    public static string Votes(string sourceId, string membersJobId) => $"{Prefix}{sourceId}/votes/{membersJobId}";

    public static int CurrentCongress(DateOnly today) => VoteSession.ForYear(today.Year).Congress;
}
