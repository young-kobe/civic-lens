namespace CivicLens.Infrastructure.Documents.Persistence;

internal sealed class DocumentComparisonHunkRow
{
    public string ComparisonId { get; set; } = string.Empty;
    public int Position { get; set; }
    public int BeforeStart { get; set; }
    public int BeforeLength { get; set; }
    public int AfterStart { get; set; }
    public int AfterLength { get; set; }
    public int BeforeContextStart { get; set; }
    public int BeforeContextLength { get; set; }
    public int AfterContextStart { get; set; }
    public int AfterContextLength { get; set; }
    public int WordEditCount { get; set; }
    public List<DocumentComparisonWordEditRow> WordEdits { get; set; } = [];
}
