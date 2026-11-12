namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary><c>Notices</c> holds the correction notices in position order. <c>Targets</c> holds the executive orders and proclamations that the disposition notes amend or revoke, in position order. Other references stay in the notes and are not here.</summary>
public sealed record ExecutiveActionDetail(int Release, ExecutiveActionRow ExecutiveAction, ExecutiveActionNoticeRow[] Notices, ExecutiveActionTargetRow[] Targets);
