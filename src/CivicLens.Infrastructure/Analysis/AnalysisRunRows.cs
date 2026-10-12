using System.Text.Json;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;

namespace CivicLens.Infrastructure.Analysis;

internal static class AnalysisRunRows
{
    public static TRow Create<TRow>(AnalysisRun run) where TRow : class, IAnalysisRunRow, new() => new()
    {
        RunId = run.RunId,
        SubjectId = run.ComparisonId,
        Task = run.Task,
        TaskVersion = run.TaskVersion,
        Model = run.Model,
        PromptVersion = run.PromptVersion,
        SchemaVersion = run.SchemaVersion,
        InputHash = run.InputHash,
        PreviousRunId = run.PreviousRunId,
        Outcome = run.Outcome,
        InputTokens = run.Usage.InputTokens,
        OutputTokens = run.Usage.OutputTokens,
        CacheReadTokens = run.Usage.CacheReadTokens,
        CacheWriteTokens = run.Usage.CacheWriteTokens,
        ChargedTokens = run.ChargedTokens,
        StopReason = run.StopReason,
        ProviderRequestId = run.ProviderRequestId,
        OutputJson = run.OutputJson,
        ValidationJson = run.ValidationErrors.IsEmpty ? null : JsonSerializer.Serialize(run.ValidationErrors),
        ContextJson = run.ContextJson,
        StartedAtUtcTicks = run.StartedAtUtc.UtcTicks,
        FinishedAtUtcTicks = run.FinishedAtUtc.UtcTicks
    };

    public static AnalysisRun ToRun(IAnalysisRunRow row)
    {
        var run = new AnalysisRun(row.RunId, row.SubjectId, row.Task, row.TaskVersion, row.Model, row.PromptVersion,
            row.SchemaVersion, row.InputHash, row.PreviousRunId, row.Outcome,
            new AnalysisTokenUsage(row.InputTokens, row.OutputTokens, row.CacheReadTokens, row.CacheWriteTokens), row.ChargedTokens,
            row.StopReason, row.ProviderRequestId, row.OutputJson,
            row.ValidationJson is null ? [] : [.. JsonSerializer.Deserialize<string[]>(row.ValidationJson)!],
            row.ContextJson, new DateTimeOffset(row.StartedAtUtcTicks, TimeSpan.Zero),
            new DateTimeOffset(row.FinishedAtUtcTicks, TimeSpan.Zero));
        run.Validate();
        return run;
    }
}
