namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseVoteBillRow
{
    public int ReleaseNumber { get; set; }
    public string VoteRecordId { get; set; } = string.Empty;
    public string BillRecordId { get; set; } = string.Empty;
}
