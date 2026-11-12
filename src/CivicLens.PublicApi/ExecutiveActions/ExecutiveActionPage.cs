namespace CivicLens.PublicApi.ExecutiveActions;

public sealed record ExecutiveActionPage(int Release, ExecutiveActionRow[] Actions, string? NextCursor);
