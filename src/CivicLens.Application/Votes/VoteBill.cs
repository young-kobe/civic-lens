using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Votes;

public sealed record VoteBill(string VoteRecordId, int Congress, string BillType, int BillNumber)
{
    public static VoteBill? Of(VoteDraftRevision vote) =>
        vote.BillType is null ? null : new(vote.RecordId, vote.Congress, vote.BillType, vote.BillNumber!.Value);

    public static VoteBill? Of(PublishedVote vote) =>
        vote.BillType is null ? null : new(vote.RecordId, vote.Congress, vote.BillType, vote.BillNumber!.Value);
}
