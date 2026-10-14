namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillPageRow
{
    public string JobId { get; set; } = "";
    public string DetailJobId { get; set; } = "";
    public BillPageRole Role { get; set; }
    public int PageOrder { get; set; }
    public string ExtractionId { get; set; } = "";
    public bool ClosesChain { get; set; }
}
