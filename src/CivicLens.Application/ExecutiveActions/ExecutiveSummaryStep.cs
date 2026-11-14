using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.ExecutiveActions;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>Writes the summary of the body text of one executive action, as the next revision of its record. It follows <see cref="Actions.ActionDraftStep"/>:
/// the same queue, run, repair pass, and code approval. A record that already holds a summary, and a rejected record, settle with no model call.</summary>
public sealed class ExecutiveSummaryStep(IExecutiveSummaryStore store, IExecutiveSummaryModel model, ExecutiveSummarySettings settings)
    : IAnalysisTaskStep<ExecutiveSummaryRun>
{
    public IAnalysisQueue<ExecutiveSummaryRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => ExecutiveSummaryTask.Identity;

    public ExecutiveSummaryRun Wrap(Core.Analysis.AnalysisRun run) => new(run, null);

    public async Task<bool> RunAsync(IAnalysisAttempt<ExecutiveSummaryRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        return evidence is null
            ? await attempt.CompleteAsync(State.Blocked, ExecutiveSummaryErrorCodes.EvidenceUnavailable, cancellationToken)
            : await SummarizeAsync(attempt, evidence, cancellationToken);
    }

    /// <summary>Runs the step on evidence that the caller read. The measure calls this with the summary of the record cleared.</summary>
    public async Task<bool> SummarizeAsync(IAnalysisAttempt<ExecutiveSummaryRun> attempt, ExecutiveSummaryEvidence evidence,
        CancellationToken cancellationToken)
    {
        attempt.ContextJson = ExecutiveSummaryPrompt.SerializeContext(evidence);
        if (evidence.IsRejected) return await SettleWithoutCallAsync(attempt, evidence, ExecutiveSummaryErrorCodes.RecordRejected, cancellationToken);
        if (evidence.Current.Summary is not null)
            return await SettleWithoutCallAsync(attempt, evidence, ExecutiveSummaryErrorCodes.Unchanged, cancellationToken);
        var first = ExecutiveSummaryPrompt.Build(evidence, settings.PromptByteCap);
        if (first is null) return await attempt.CompleteAsync(State.Blocked, ExecutiveSummaryErrorCodes.OverByteCap, cancellationToken);
        var loop = new AnalysisValidationLoop<ExecutiveSummaryRun, ExecutiveActionRevision>(Wrap, attempt, Core.Analysis.AnalysisRunOutcome.Drafted,
            AiTextRules.MaximumValidationPasses,
            errors => errors.IsDefaultOrEmpty ? first : ExecutiveSummaryPrompt.Build(evidence, settings.PromptByteCap, errors), model.DraftAsync,
            (json, run) => ExecutiveSummaryResolver.Resolve(json, evidence, run.FinishedAtUtc), ReadRunAsync);
        return await RunLoopAsync(attempt, loop, cancellationToken);
    }

    private async Task<Core.Analysis.AnalysisRun?> ReadRunAsync(string runId, CancellationToken cancellationToken) =>
        (await store.GetRunAsync(runId, cancellationToken))?.Run;

    private async Task<bool> RunLoopAsync(IAnalysisAttempt<ExecutiveSummaryRun> attempt,
        AnalysisValidationLoop<ExecutiveSummaryRun, ExecutiveActionRevision> loop, CancellationToken cancellationToken)
    {
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(Wrap(result.Run!), ExecutiveSummaryCheckpoint.From(attempt.Claim, result.Accepted!), cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null => await attempt.CompleteAsync(State.Blocked, ExecutiveSummaryErrorCodes.OverByteCap, cancellationToken),
            AnalysisLoopEnd.Exhausted or AnalysisLoopEnd.NotSent => await loop.FailAsync(result, cancellationToken),
            _ => result.Progress
        };
    }

    private static Task<bool> SettleWithoutCallAsync(IAnalysisAttempt<ExecutiveSummaryRun> attempt, ExecutiveSummaryEvidence evidence, string code,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(ExecutiveSummaryTask.Model, evidence.Text.ExtractionId, null);
        return attempt.SettleAsync(new ExecutiveSummaryRun(run, code), State.Succeeded, code, cancellationToken);
    }
}
