namespace CivicLens.PublicApi.Officials;

/// <summary>
/// One official with the summary figures of the official page. <c>Congress</c> is the newest Congress with a roll call that lists the official, and it is null when none does.
/// <c>Bills</c> and <c>Issues</c> are null then. <c>ExecutiveActions</c> is null unless the latest term is an executive term. Every figure names its scope.
/// </summary>
public sealed record OfficialDetail(int Release, OfficialRow Official, string RecordCountScope, OfficialTerm[] Terms, int? Congress, MissedVotesFigure[] MissedVotes,
    BillCountsFigure? Bills, IssueCountsFigures? Issues, ExecutiveActionsFigure? ExecutiveActions);
