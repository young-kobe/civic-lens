using System.Collections.Immutable;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

/// <summary>
/// The one owner of an attempt that keeps everything in memory, so a drafting step runs with no queue and no database write. The owner measures use it.
/// It charges the spend cap with the same reservation rule that the worker uses. A measure that sends one prompt of a step with two prompts passes
/// <paramref name="measuredPromptVersion"/>, and the attempt refuses any other prompt of that step. A request that names no prompt version runs under the
/// version of the task identity.
/// </summary>
internal sealed class AnalysisMeasureAttempt<TRun, TRevision>(string candidateId, long tokenCap, long spentBefore, AnalysisTaskIdentity identity,
    Func<TRun, AnalysisRun> runOf, Func<AnalysisCheckpoint, TRevision?> revisionOf, string? measuredPromptVersion = null) : IAnalysisAttempt<TRun>
    where TRun : class
    where TRevision : class
{
    private readonly List<AnalysisRun> runs = [];
    private AnalysisModelRequest? request;
    private string? previousRunId;

    public AnalysisClaim Claim { get; } = new(new(candidateId, identity.Task, AnalysisState.Running, "measure", 0, 1, null, null), "measure", 0);
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
        RefusedOtherPrompt = measuredPromptVersion is not null && modelRequest.PromptVersion != measuredPromptVersion;
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
        new(Guid.NewGuid().ToString("N"), candidateId, identity.Task, identity.TaskVersion, model, identity.PromptVersion,
            identity.SchemaVersion, inputHash, previous, AnalysisRunOutcome.Bypassed, AnalysisTokenUsage.None, 0, null, null, null, [],
            ContextJson, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

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
        new(RunId!, candidateId, identity.Task, identity.TaskVersion, request!.Model, request.PromptVersion ?? identity.PromptVersion,
            request.SchemaVersion ?? identity.SchemaVersion, request.InputHash, previousRunId, outcome, usage, Math.Max(charged, usage.Total), stopReason,
            requestId, outputJson, [], ContextJson, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
