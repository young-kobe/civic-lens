using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipAnalysisRunRow : IAnalysisRunRow
{
    public string RunId { get; set; } = string.Empty;
    public string PairId { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public string TaskVersion { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public string? PreviousRunId { get; set; }
    public AnalysisRunOutcome Outcome { get; set; }
    public string? Reason { get; set; }
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
    public long StartedAtUtcTicks { get; set; }
    public long FinishedAtUtcTicks { get; set; }

    string IAnalysisRunRow.SubjectId
    {
        get => PairId;
        set => PairId = value;
    }
}
