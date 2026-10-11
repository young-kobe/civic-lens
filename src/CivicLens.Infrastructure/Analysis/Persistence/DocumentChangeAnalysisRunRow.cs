using CivicLens.Core.Analysis;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class DocumentChangeAnalysisRunRow
{
    public string RunId { get; set; } = string.Empty;
    public string ComparisonId { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public string TaskVersion { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public string? PreviousRunId { get; set; }
    public AnalysisRunOutcome Outcome { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public long ChargedTokens { get; set; }
    public string? StopReason { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? OutputJson { get; set; }
    public string? ValidationJson { get; set; }
    public string? ContextJson { get; set; }
    public string? DraftId { get; set; }
    public int? RevisionNumber { get; set; }
    public long StartedAtUtcTicks { get; set; }
    public long FinishedAtUtcTicks { get; set; }
}
