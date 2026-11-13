using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>One row of the view <c>public_api.executive_action_summaries</c> with its sentences in position order. The label is set only for the origin Model.</summary>
public sealed record ExecutiveActionSummaryRow(PublishedTextOrigin TextOrigin, string? Label, bool Cut, string[] Sentences);
