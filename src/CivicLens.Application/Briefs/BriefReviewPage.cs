using System.Collections.Immutable;

namespace CivicLens.Application.Briefs;

public sealed record BriefReviewPage(ImmutableArray<BriefReviewItem> Items, string? NewerCursor, string? OlderCursor);
