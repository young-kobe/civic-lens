namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedBillHistoryRow
{
    public string DetailJobId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public int Congress { get; set; }
    public string BillType { get; set; } = string.Empty;
    public int BillNumber { get; set; }
    public long ObservedAtUtcTicks { get; set; }
    public int ActionCount { get; set; }
    public string? PublicLaw { get; set; }
}
