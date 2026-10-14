namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class ActionCandidateRow
{
    public string CandidateId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public int Congress { get; set; }
    public string BillType { get; set; } = "";
    public int BillNumber { get; set; }
    public long UpdateDateUtcTicks { get; set; }
    public string DetailJobId { get; set; } = "";
    public string ConfigurationRevisionId { get; set; } = "";
    public long CreatedAtUtcTicks { get; set; }
    public bool IncludesSummaries { get; set; }
}
