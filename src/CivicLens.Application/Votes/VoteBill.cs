using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteBill(string VoteRecordId, int Congress, string BillType, int BillNumber)
{
    public static VoteBill? Of(VoteDraftRevision vote) => Of(vote.RecordId, vote.Congress, vote.BillType, vote.BillNumber);

    public static VoteBill? Of(string voteRecordId, int congress, string? billType, int? billNumber) =>
        billType is null ? null : new(voteRecordId, congress, billType, billNumber!.Value);
}
