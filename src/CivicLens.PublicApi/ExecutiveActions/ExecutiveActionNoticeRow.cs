namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>One correction notice of an executive action, from the view <c>public_api.executive_action_notices</c>.</summary>
public sealed record ExecutiveActionNoticeRow(int Position, string DocumentNumber, DateOnly PublishedOn, string Url);
