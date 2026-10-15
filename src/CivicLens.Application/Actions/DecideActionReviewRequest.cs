using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed record DecideActionReviewRequest(string RecordId, int ExpectedRevisionNumber, int ExpectedReviewStateVersion,
    ReviewDecisionKind Kind, string? Note, ImmutableArray<string> ResolvedDecisionIds, string IdempotencyKey);
