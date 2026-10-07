namespace CivicLens.Infrastructure.Documents.Persistence;

internal sealed class DocumentComparisonRow
{
    public string ComparisonId { get; set; } = string.Empty;
    public string BeforeExtractionId { get; set; } = string.Empty;
    public string AfterExtractionId { get; set; } = string.Empty;
    public string AlgorithmVersion { get; set; } = string.Empty;
    public string SettingsVersion { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
}
