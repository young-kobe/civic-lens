using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed record DecideVoteReviewRequest(string RecordId, int ExpectedRevisionNumber, int ExpectedReviewStateVersion,
    ReviewDecisionKind Kind, string? Note, ImmutableArray<string> ResolvedDecisionIds, string IdempotencyKey,
    string? CorrectionNote = null);
