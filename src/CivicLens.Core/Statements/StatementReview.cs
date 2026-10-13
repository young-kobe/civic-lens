using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Core.Statements;

public sealed record StatementReview(string CandidateId, StatementDraftRevision? CurrentRevision, int ReviewStateVersion,
    ReviewStatus CurrentStatus, ImmutableArray<StatementDraftRevision> Revisions, ImmutableArray<ReviewDecision> Decisions,
    ImmutableArray<ReviewDecision> UnresolvedConcerns);
