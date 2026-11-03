using System.Collections.Immutable;

namespace CivicLens.Application.Briefs;

public sealed record BriefCheckResult(int RevisionNumber, BriefCheckRecommendation Recommendation, ImmutableArray<BriefCheckFlag> Flags)
{
    public const int MaximumFlags = 24;
}

public enum BriefCheckRecommendation
{
    ApproveRecommended,
    NeedsAttention
}
