using System.Collections.Immutable;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

/// <summary>An attempt that keeps everything in memory, so a step runs with no queue and no database write. The text measure of bills and the summary
/// measure of executive actions both use it. It charges the spend cap with the same reservation rule that the worker uses, and it sends only the
/// prompt versions that the measure names. <typeparamref name="TRevision"/> is the revision that an accepted settlement carries.</summary>
internal sealed class AnalysisMeasureAttempt<TRun, TRevision>(string subjectId, AnalysisTaskIdentity identity, IReadOnlySet<string> measuredPrompts,
    Func<TRun, AnalysisRun> runOf, Func<AnalysisCheckpoint, TRevision?> revisionOf, long tokenCap, long spentBefore) : IAnalysisAttempt<TRun>
    where TRun : class where TRevision : class
{
    private readonly List<AnalysisRun> runs = [];
    private AnalysisModelRequest? request;
    private string? previousRunId;

    public AnalysisClaim Claim { get; } = new(new(subjectId, identity.Task, AnalysisState.Running, "measure", 0, 1, null, null), "measure", 0);
    public string? ContextJson { get; set; }
    public string? RunId { get; private set; }
    public long SpentTokens { get; private set; } = spentBefore;
    public bool StoppedByCap { get; private set; }
    public bool RefusedOtherPrompt { get; private set; }
    public int Calls { get; private set; }
    public TRevision? Revision { get; private set; }
    public string? ErrorCode { get; private set; }
    public AnalysisModelFailure? Failure { get; private set; }
    public ImmutableArray<AnalysisRun> Runs => [.. runs];

    public async Task<AnalysisCall> CallModelAsync(AnalysisModelRequest modelRequest, string? previous,
        Func<AnalysisModelRequest, CancellationToken, Task<AnalysisModelResponse>> send, CancellationToken cancellationToken)
    {
        RefusedOtherPrompt = !measuredPrompts.Contains(modelRequest.PromptVersion ?? string.Empty);
        StoppedByCap = !RefusedOtherPrompt && SpentTokens + modelRequest.ReservedTokens > tokenCap;
        if (RefusedOtherPrompt || StoppedByCap) return new(null, false);
        (RunId, previousRunId, request) = (Guid.NewGuid().ToString("N"), previous, modelRequest);
        try
        {
            var response = await send(modelRequest, cancellationToken);
            (Calls, SpentTokens) = (Calls + 1, SpentTokens + response.Usage.Total);
            return new(response, false);
        }
        catch (AnalysisModelException exception)
        {
            var outcome = Enum.Parse<AnalysisRunOutcome>(exception.Kind.ToString());
            var charged = AnalysisFailureRules.ChargeFor(exception, modelRequest.ReservedTokens);
            var run = Run(outcome, exception.Usage ?? AnalysisTokenUsage.None, charged, null, null, null);
            runs.Add(run);
            (Calls, SpentTokens, ErrorCode, Failure) = (Calls + 1, SpentTokens + run.ChargedTokens, AnalysisErrorCodes.ForOutcome(outcome), exception.Kind);
            return new(null, false);
        }
    }

    public AnalysisRun ResponseRun(AnalysisRunOutcome outcome, AnalysisModelResponse response) =>
        Run(outcome, response.Usage, response.Usage.Total, response.StopReason, response.RequestId, response.OutputJson);

    public AnalysisRun BypassRun(string model, string inputHash, string? previous) =>
        new(Guid.NewGuid().ToString("N"), subjectId, identity.Task, identity.TaskVersion, model, identity.PromptVersion, identity.SchemaVersion,
            inputHash, previous, AnalysisRunOutcome.Bypassed, AnalysisTokenUsage.None, 0, null, null, null, [], ContextJson,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public Task<bool> SettleAsync(TRun run, AnalysisCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        runs.Add(runOf(run));
        if (revisionOf(checkpoint) is { } revision) Revision = revision;
        ErrorCode = checkpoint.ErrorCode;
        return Task.FromResult(true);
    }

    public Task<bool> SettleAsync(TRun run, AnalysisState state, string? errorCode, CancellationToken cancellationToken)
    {
        runs.Add(runOf(run));
        ErrorCode = errorCode;
        return Task.FromResult(true);
    }

    public Task<bool> CompleteAsync(AnalysisState state, string errorCode, CancellationToken cancellationToken)
    {
        ErrorCode = errorCode;
        return Task.FromResult(true);
    }

    private AnalysisRun Run(AnalysisRunOutcome outcome, AnalysisTokenUsage usage, long charged, string? stopReason, string? requestId,
        string? outputJson) =>
        new(RunId!, subjectId, identity.Task, identity.TaskVersion, request!.Model, request.PromptVersion!, request.SchemaVersion!,
            request.InputHash, previousRunId, outcome, usage, Math.Max(charged, usage.Total), stopReason, requestId, outputJson, [], ContextJson,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
