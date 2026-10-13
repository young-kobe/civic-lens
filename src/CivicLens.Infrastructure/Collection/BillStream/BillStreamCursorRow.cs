namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillStreamCursorRow
{
    public string SourceId { get; set; } = "";
    public DateOnly? LastProcessedDay { get; set; }
    public int PassAdmittedBills { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
}
