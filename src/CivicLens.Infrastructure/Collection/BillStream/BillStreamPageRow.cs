namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillStreamPageRow
{
    public string ListJobId { get; set; } = "";
    public long RecordedAtUtcTicks { get; set; }
    public int UnknownTypeCount { get; set; }
}
