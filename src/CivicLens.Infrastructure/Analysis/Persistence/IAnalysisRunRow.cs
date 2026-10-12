using CivicLens.Core.Analysis;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal interface IAnalysisRunRow
{
    string RunId { get; set; }
    string SubjectId { get; set; }
    string Task { get; set; }
    string TaskVersion { get; set; }
    string Model { get; set; }
    string PromptVersion { get; set; }
    string SchemaVersion { get; set; }
    string InputHash { get; set; }
    string? PreviousRunId { get; set; }
    AnalysisRunOutcome Outcome { get; set; }
    long InputTokens { get; set; }
    long OutputTokens { get; set; }
    long CacheReadTokens { get; set; }
    long CacheWriteTokens { get; set; }
    long ChargedTokens { get; set; }
    string? StopReason { get; set; }
    string? ProviderRequestId { get; set; }
    string? OutputJson { get; set; }
    string? ValidationJson { get; set; }
    string? ContextJson { get; set; }
    long StartedAtUtcTicks { get; set; }
    long FinishedAtUtcTicks { get; set; }
}
