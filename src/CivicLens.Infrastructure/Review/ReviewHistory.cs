using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review;

internal sealed record ReviewHistory<TRevision>(ImmutableArray<TRevision> Revisions, ImmutableArray<ReviewDecision> Decisions,
    ImmutableArray<ReviewDecision> UnresolvedConcerns, ReviewStatus CurrentStatus) where TRevision : class
{
    public TRevision? CurrentRevision => Revisions.IsEmpty ? null : Revisions[^1];
}
