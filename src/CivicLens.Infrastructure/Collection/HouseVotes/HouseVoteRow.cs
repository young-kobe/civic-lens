namespace CivicLens.Infrastructure.Collection.HouseVotes;

internal sealed class HouseVoteRow
{
    public string DetailJobId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public int Congress { get; set; }
    public int SessionNumber { get; set; }
    public int RollCallNumber { get; set; }
    public long UpdateDateUtcTicks { get; set; }
    public string? BillType { get; set; }
    public int? BillNumber { get; set; }
    public string ExtractionId { get; set; } = "";
}
