using System.Collections.Immutable;

namespace CivicLens.Core.Analysis;

public sealed record AnalysisRun(string RunId, string ComparisonId, string Task, string TaskVersion, string Model,
    string PromptVersion, string SchemaVersion, string InputHash, string? PreviousRunId, AnalysisRunOutcome Outcome,
    AnalysisTokenUsage Usage, long ChargedTokens, string? StopReason, string? ProviderRequestId, string? OutputJson,
    ImmutableArray<string> ValidationErrors, string? ContextJson, DateTimeOffset StartedAtUtc, DateTimeOffset FinishedAtUtc)
{
    public const int MaximumValidationErrors = 64;
    public const int MaximumValidationErrorLength = 512;
    public const int MaximumOutputLength = 1_000_000;

    public void Validate()
    {
        if (!IsId(RunId, 32) || !IsId(ComparisonId, 64) || !IsId(InputHash, 64) ||
            PreviousRunId is not null && !IsId(PreviousRunId, 32))
            throw new ArgumentException("Analysis run identity is invalid.");
        if (!IsLabel(Task) || !IsLabel(TaskVersion) || !IsLabel(Model) || !IsLabel(PromptVersion) || !IsLabel(SchemaVersion) ||
            StopReason is not null && !IsLabel(StopReason) || ProviderRequestId is not null && !IsLabel(ProviderRequestId))
            throw new ArgumentException("Analysis run labels are invalid.");
        if (!Enum.IsDefined(Outcome) || Usage is null || !Usage.IsValid || ChargedTokens < Usage.Total)
            throw new ArgumentException("Analysis run usage is invalid.");
        if (OutputJson is { Length: > MaximumOutputLength } || OutputJson?.Contains('\0') == true)
            throw new ArgumentException("Analysis run output is invalid.");
        if ((Outcome is AnalysisRunOutcome.Drafted or AnalysisRunOutcome.DraftDiscarded) && OutputJson is null)
            throw new ArgumentException("A drafted run must retain its structured output.");
        if (ValidationErrors.IsDefault || ValidationErrors.Length > MaximumValidationErrors ||
            ValidationErrors.Any(error => string.IsNullOrWhiteSpace(error) || error.Length > MaximumValidationErrorLength))
            throw new ArgumentException("Analysis run validation errors are invalid.");
        var rejected = Outcome is AnalysisRunOutcome.CitationRejected or AnalysisRunOutcome.OutputRejected;
        if (rejected == ValidationErrors.IsEmpty)
            throw new ArgumentException("Only a rejected run retains validation errors, and it must retain at least one.");
        if ((ContextJson is null) != (Outcome == AnalysisRunOutcome.Interrupted) || ContextJson is { Length: > MaximumOutputLength })
            throw new ArgumentException("Only an interrupted run, recovered without its inputs, can omit its context.");
        if (StartedAtUtc.Offset != TimeSpan.Zero || FinishedAtUtc.Offset != TimeSpan.Zero || FinishedAtUtc < StartedAtUtc)
            throw new ArgumentException("Analysis run times are invalid.");
    }

    private static bool IsId(string? value, int length) => value is not null && value.Length == length &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsLabel(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Contains('\0');
}
