using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed record DecideStatementReviewRequest(string CandidateId, int ExpectedRevisionNumber, int ExpectedReviewStateVersion,
    ReviewDecisionKind Kind, string? Note, ImmutableArray<string> ResolvedDecisionIds, string IdempotencyKey,
    string? CorrectionNote = null);
