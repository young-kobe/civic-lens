namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary><c>Notices</c> holds the correction notices in position order.</summary>
public sealed record ExecutiveActionDetail(int Release, ExecutiveActionRow ExecutiveAction, ExecutiveActionNoticeRow[] Notices);
