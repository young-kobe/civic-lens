using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

/// <summary>An attempt that keeps everything in memory, so the step runs with no queue and no database write.
/// It charges the spend cap with the same reservation rule that the worker uses, and it sends only the text prompt.</summary>
internal sealed class ActionTextMeasureAttempt(string candidateId, long tokenCap, long spentBefore) : IAnalysisAttempt<ActionDraftRun>
{
    private readonly List<AnalysisRun> runs = [];
    private AnalysisModelRequest? request;
    private string? previousRunId;

    public AnalysisClaim Claim { get; } = new(new(candidateId, ActionDraftTask.Task, AnalysisState.Running, "measure", 0, 1, null, null), "measure", 0);
    public string? ContextJson { get; set; }
    public string? RunId { get; private set; }
    public long SpentTokens { get; private set; } = spentBefore;
    public bool StoppedByCap { get; private set; }
    public bool RefusedOtherPrompt { get; private set; }
    public int Calls { get; private set; }
    public ActionDraftRevision? Revision { get; private set; }
    public string? ErrorCode { get; private set; }
    public AnalysisModelFailure? Failure { get; private set; }
    public ImmutableArray<AnalysisRun> Runs => [.. runs];

    public async Task<AnalysisCall> CallModelAsync(AnalysisModelRequest modelRequest, string? previous,
        Func<AnalysisModelRequest, CancellationToken, Task<AnalysisModelResponse>> send, CancellationToken cancellationToken)
    {
        RefusedOtherPrompt = modelRequest.PromptVersion != ActionDraftTask.TextPromptVersion;
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
            runs.Add(Run(outcome, exception.Usage ?? AnalysisTokenUsage.None, charged, null, null, null));
            (Calls, SpentTokens, ErrorCode, Failure) = (Calls + 1, SpentTokens + charged, AnalysisErrorCodes.ForOutcome(outcome), exception.Kind);
            return new(null, false);
        }
    }

    public AnalysisRun ResponseRun(AnalysisRunOutcome outcome, AnalysisModelResponse response) =>
        Run(outcome, response.Usage, response.Usage.Total, response.StopReason, response.RequestId, response.OutputJson);

    public AnalysisRun BypassRun(string model, string inputHash, string? previous) =>
        new(Guid.NewGuid().ToString("N"), candidateId, ActionDraftTask.Task, ActionDraftTask.TaskVersion, model, ActionDraftTask.PromptVersion,
            ActionDraftTask.SchemaVersion, inputHash, previous, AnalysisRunOutcome.Bypassed, AnalysisTokenUsage.None, 0, null, null, null, [],
            ContextJson, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public Task<bool> SettleAsync(ActionDraftRun run, AnalysisCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        runs.Add(run.Run);
        if (checkpoint is ActionDraftCheckpoint { Revision: { } revision }) Revision = revision;
        ErrorCode = checkpoint.ErrorCode;
        return Task.FromResult(true);
    }

    public Task<bool> SettleAsync(ActionDraftRun run, AnalysisState state, string? errorCode, CancellationToken cancellationToken)
    {
        runs.Add(run.Run);
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
        new(RunId!, candidateId, ActionDraftTask.Task, ActionDraftTask.TaskVersion, request!.Model, request.PromptVersion!, request.SchemaVersion!,
            request.InputHash, previousRunId, outcome, usage, Math.Max(charged, usage.Total), stopReason, requestId, outputJson, [], ContextJson,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
