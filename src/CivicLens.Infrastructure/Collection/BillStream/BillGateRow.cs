using CivicLens.Application.Collection.Bills;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillGateRow
{
    public string DetailJobId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public int Congress { get; set; }
    public string BillType { get; set; } = "";
    public int BillNumber { get; set; }
    public long UpdateDateUtcTicks { get; set; }
    public string? PolicyArea { get; set; }
    public string IssueRevisionId { get; set; } = "";
    public BillGateDecision Decision { get; set; }
    public BillGateReason? Reason { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long? DecidedAtUtcTicks { get; set; }
}
