using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed record DecideDocumentChangeReviewRequest(string DraftId, int ExpectedRevisionNumber,
    int ExpectedReviewStateVersion, ReviewDecisionKind Kind, string? Note,
    ImmutableArray<string> ResolvedDecisionIds, string IdempotencyKey);
