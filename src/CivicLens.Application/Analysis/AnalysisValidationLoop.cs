using System.Collections.Immutable;
using CivicLens.Core.Analysis;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Analysis;

public sealed class AnalysisValidationLoop<TRun, TAccepted>(Func<AnalysisRun, TRun> wrap, IAnalysisAttempt<TRun> attempt,
    AnalysisRunOutcome completedOutcome, int maximumPasses, Func<ImmutableArray<string>, AnalysisModelRequest?> buildRequest,
    Func<AnalysisModelRequest, CancellationToken, Task<AnalysisModelResponse>> send,
    Func<string, AnalysisRun, AnalysisResolution<TAccepted>> resolve,
    Func<string, CancellationToken, Task<AnalysisRun?>> readRun) where TRun : class
{
    public async Task<AnalysisLoopResult<TAccepted>> RunAsync(CancellationToken cancellationToken)
    {
        var number = 1;
        AnalysisRun? resumedFrom = null;
        ImmutableArray<string> errors = default;
        if (attempt.Claim.Record.RetryOfRunId is { } retryOf)
        {
            resumedFrom = await readRun(retryOf, cancellationToken)
                ?? throw new InvalidDataException("The rejected run that a retry continues is missing.");
            (number, errors) = (2, resumedFrom.ValidationErrors);
        }
        var request = buildRequest(errors);
        if (request is null) return AnalysisLoopResult<TAccepted>.NotSent(resumedFrom);
        var previousRunId = resumedFrom?.RunId;
        while (true)
        {
            var call = await attempt.CallModelAsync(request, previousRunId, send, cancellationToken);
            if (call.Response is not { } response) return AnalysisLoopResult<TAccepted>.Settled(call.Progress);
            var runId = attempt.RunId!;
            var outcome = response.StopOutcome(completedOutcome);
            var run = attempt.ResponseRun(outcome, response);
            if (outcome != completedOutcome)
                return AnalysisLoopResult<TAccepted>.Settled(await attempt.SettleAsync(wrap(run), State.Failed,
                    AnalysisErrorCodes.ForOutcome(outcome), cancellationToken));

            var resolution = resolve(response.OutputJson!, run);
            if (resolution.IsAccepted) return AnalysisLoopResult<TAccepted>.AcceptedBy(run, resolution.Accepted!);

            var rejected = run with
            {
                Outcome = resolution.HasCitationErrors ? AnalysisRunOutcome.CitationRejected : AnalysisRunOutcome.OutputRejected,
                ValidationErrors = resolution.Errors
            };
            var retry = number < maximumPasses ? buildRequest(resolution.Errors) : null;
            if (retry is null) return AnalysisLoopResult<TAccepted>.ExhaustedAfter(rejected);
            if (!await attempt.SettleAsync(wrap(rejected), State.Running, null, cancellationToken))
                return AnalysisLoopResult<TAccepted>.Settled(false);
            (number, previousRunId, request) = (number + 1, runId, retry);
        }
    }
}
