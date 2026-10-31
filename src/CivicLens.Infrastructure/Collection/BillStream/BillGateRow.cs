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
    public int CosponsorCount { get; set; }
    public int SummaryCount { get; set; }
    public int? SubjectCount { get; set; }
    public bool LatestActionRead { get; set; }
    public DateOnly? LatestActionDate { get; set; }
    public string? LatestActionText { get; set; }
    public string IssueRevisionId { get; set; } = "";
    public bool HasTrackedIssue { get; set; }
    public BillGateDecision Decision { get; set; }
    public BillGateReason? Reason { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long? DecidedAtUtcTicks { get; set; }

    public bool HasCosponsorChain => CosponsorCount > 0;
    public bool HasSummariesChain => SummaryCount > 0;
    public bool HasSubjectsChain => SubjectCount is { } count && CongressBill.HasLegislativeSubjects(count, PolicyArea);
}
