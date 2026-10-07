namespace CivicLens.Infrastructure.Documents.Persistence;

internal sealed class DocumentExtractionRow
{
    public string ExtractionId { get; set; } = null!;
    public string AttemptId { get; set; } = null!;
    public string ParserVersion { get; set; } = null!;
    public string NormalizationVersion { get; set; } = null!;
    public string Text { get; set; } = null!;
    public string TextSha256 { get; set; } = null!;
}
