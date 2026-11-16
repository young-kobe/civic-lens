namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>One page of the executive action list. <c>ActionsInRelease</c> counts every executive action of the release. A filter does not change it.
/// <c>PreviousCursor</c> is null on the first page.</summary>
public sealed record ExecutiveActionPage(int Release, int ActionsInRelease, ExecutiveActionRow[] Actions, string? NextCursor, string? PreviousCursor);
