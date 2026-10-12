using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record StatementReviewListItem(string CandidateId, string SourceId, string Url, StatementDraftRevision? CurrentRevision,
    int ReviewStateVersion, ReviewStatus CurrentStatus, int UnresolvedConcernCount);
