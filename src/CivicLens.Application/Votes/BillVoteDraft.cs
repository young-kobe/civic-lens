using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record BillVoteDraft(string RecordId, Chamber Chamber, int SessionNumber, int RollCallNumber, VoteKind Kind, DateOnly VotedOn, ReviewStatus Status)
{
    public const int MaximumPerBill = 25;
}
