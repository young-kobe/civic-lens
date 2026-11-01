namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillTextVersionRow
{
    public int Congress { get; set; }
    public string BillType { get; set; } = "";
    public int BillNumber { get; set; }
    public string PackageId { get; set; } = "";
    public string VersionType { get; set; } = "";
    public DateOnly? VersionDate { get; set; }
    public string SourceUrl { get; set; } = "";
    public string ExtractionId { get; set; } = "";
    public int CharsTotal { get; set; }
    public int CharsRead { get; set; }
    public string ContentSha256 { get; set; } = "";
}
