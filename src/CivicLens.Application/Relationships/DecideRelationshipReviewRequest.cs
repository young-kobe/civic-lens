using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Relationships;

public sealed record DecideRelationshipReviewRequest(string RecordId, int ExpectedRevisionNumber, int ExpectedReviewStateVersion,
    ReviewDecisionKind Kind, string? Note, ImmutableArray<string> ResolvedDecisionIds, string IdempotencyKey);
