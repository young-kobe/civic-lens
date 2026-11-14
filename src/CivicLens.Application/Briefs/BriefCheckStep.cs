using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Briefs;

public sealed class BriefCheckStep(IBriefCheckStore store, IBriefCheckModel model, BriefCheckSettings settings)
    : IAnalysisTaskStep<BriefCheckRun>
{
    public IAnalysisQueue<BriefCheckRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => BriefCheckTask.Identity;

    public BriefCheckRun Wrap(AnalysisRun run) => new(run, null, null, null, null);

    public async Task<bool> RunAsync(IAnalysisAttempt<BriefCheckRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null) return await attempt.CompleteAsync(State.Blocked, BriefDraftErrorCodes.EvidenceUnavailable, cancellationToken);
        var notCited = BriefSelection.Select(evidence.NotCited, sent => FitsWithRetryReserve(evidence, [.. sent.Select(item => item.Record)]))
            .Sent.Select(item => item.Record).ToImmutableArray();
        var first = Prompt(evidence, notCited, default);
        if (first is null) return await attempt.CompleteAsync(State.Blocked, BriefDraftErrorCodes.OverByteCap, cancellationToken);
        attempt.ContextJson = BriefCheckPrompt.SerializeContext(evidence.Revision, notCited, Omitted(evidence, notCited));
        if (evidence.Settled?.Fingerprint == first.InputHash) return await SettleUnchangedAsync(attempt, evidence.Settled, cancellationToken);
        return await CheckAsync(attempt, evidence, notCited, first.InputHash, cancellationToken);
    }

    private async Task<bool> CheckAsync(IAnalysisAttempt<BriefCheckRun> attempt, BriefCheckEvidence evidence,
        ImmutableArray<BriefRecord> notCited, string fingerprint, CancellationToken cancellationToken)
    {
        var loop = new AnalysisValidationLoop<BriefCheckRun, BriefCheckResult>(Wrap, attempt, AnalysisRunOutcome.Drafted,
            AiTextRules.MaximumValidationPasses, errors => Prompt(evidence, notCited, errors), model.CheckAsync,
            (json, _) => BriefCheckResolver.Resolve(json, evidence.Revision),
            async (runId, token) => (await store.GetRunAsync(runId, token))?.Run);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(new BriefCheckRun(result.Run!, null, result.Accepted, null, fingerprint), State.Succeeded, null,
                cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null =>
                await attempt.CompleteAsync(State.Blocked, BriefDraftErrorCodes.OverByteCap, cancellationToken),
            AnalysisLoopEnd.Exhausted or AnalysisLoopEnd.NotSent => await loop.FailAsync(result, cancellationToken),
            _ => result.Progress
        };
    }

    private AnalysisModelRequest? Prompt(BriefCheckEvidence evidence, ImmutableArray<BriefRecord> notCited, ImmutableArray<string> errors) =>
        BriefCheckPrompt.Build(evidence.Revision, evidence.Cited, notCited, Omitted(evidence, notCited), settings.PromptByteCap,
            errors);

    private bool FitsWithRetryReserve(BriefCheckEvidence evidence, ImmutableArray<BriefRecord> notCited) =>
        BriefCheckPrompt.Build(evidence.Revision, evidence.Cited, notCited, Omitted(evidence, notCited),
            settings.PromptByteCap - BriefDraftTask.RetryReasonReserveBytes) is not null;

    private static int Omitted(BriefCheckEvidence evidence, ImmutableArray<BriefRecord> notCited) => evidence.NotCited.Length - notCited.Length;

    private static Task<bool> SettleUnchangedAsync(IAnalysisAttempt<BriefCheckRun> attempt, BriefCheckSettled settled,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(BriefCheckTask.Model, settled.Fingerprint, null);
        return attempt.SettleAsync(new BriefCheckRun(run, BriefDraftErrorCodes.Unchanged, null, settled.RunId, null), State.Succeeded,
            BriefDraftErrorCodes.Unchanged, cancellationToken);
    }
}
