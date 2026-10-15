using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review;

internal sealed record ReviewHistory<TRevision>(TRevision? CurrentRevision, TRevision? LastApprovedRevision,
    ImmutableArray<ReviewDecision> Decisions, ImmutableArray<ReviewDecision> UnresolvedConcerns, ReviewStatus CurrentStatus)
    where TRevision : class
{
    public ImmutableArray<TRevision> LoadedRevisions => [.. new[] { LastApprovedRevision, CurrentRevision }.OfType<TRevision>().Distinct()];
}
