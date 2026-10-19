using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteReviewRead(VoteReview Review, ImmutableDictionary<string, DocumentExtraction> Extractions,
    ImmutableDictionary<string, string> PersonNames, ImmutableArray<string> BillActionRecordIds)
{
    public ApprovalCheck Check => ApprovalCheck.Of(() => Review.CurrentRevision.ValidateForApproval(Extractions));
}
