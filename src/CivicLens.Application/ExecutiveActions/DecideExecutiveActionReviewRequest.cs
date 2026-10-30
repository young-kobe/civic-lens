using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.ExecutiveActions;

public sealed record DecideExecutiveActionReviewRequest(string RecordId, int ExpectedRevisionNumber, int ExpectedReviewStateVersion,
    ReviewDecisionKind Kind, string? Note, ImmutableArray<string> ResolvedDecisionIds, string IdempotencyKey,
    string? CorrectionNote = null);
