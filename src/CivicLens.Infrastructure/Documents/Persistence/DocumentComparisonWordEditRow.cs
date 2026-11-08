namespace CivicLens.Infrastructure.Documents.Persistence;

internal sealed class DocumentComparisonWordEditRow
{
    public string ComparisonId { get; set; } = string.Empty;
    public int HunkPosition { get; set; }
    public int Position { get; set; }
    public int BeforeStart { get; set; }
    public int BeforeLength { get; set; }
    public int AfterStart { get; set; }
    public int AfterLength { get; set; }
}
