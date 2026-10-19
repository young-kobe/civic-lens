using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Core.Actions;

public sealed record ActionReview(string RecordId, ActionDraftRevision CurrentRevision, ActionDraftRevision? LastApprovedRevision,
    ImmutableArray<ActionDraftRevision> Revisions, int ReviewStateVersion, ReviewStatus CurrentStatus, ImmutableArray<ReviewDecision> Decisions,
    ImmutableArray<ReviewDecision> UnresolvedConcerns)
{
    public bool IsSponsorshipOnly => LastApprovedRevision is { } approved && CurrentRevision.IsSponsorshipOnlyChangeOf(approved);

    public bool IsBulkSafe => UnresolvedConcerns.IsEmpty && IsSponsorshipOnly;
}
