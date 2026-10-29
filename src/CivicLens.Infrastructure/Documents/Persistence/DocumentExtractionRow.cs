namespace CivicLens.Infrastructure.Documents.Persistence;

internal sealed class DocumentExtractionRow
{
    public string ExtractionId { get; set; } = null!;
    public string AttemptId { get; set; } = null!;
    public string ExtractionVersion { get; set; } = null!;
    public string Text { get; set; } = null!;
    public string TextSha256 { get; set; } = null!;
    public string? ProfileJson { get; set; }
    public int? Part { get; set; }
}
