using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Briefs;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Briefs;

public sealed class BriefDraftStep(IBriefDraftStore store, IBriefDraftModel model, BriefDraftSettings settings,
    IReadOnlyDictionary<string, string> officialNames) : IAnalysisTaskStep<BriefDraftRun>
{
    public IAnalysisQueue<BriefDraftRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => BriefDraftTask.Identity;

    public BriefDraftRun Wrap(AnalysisRun run) => new(run, null);

    public async Task<bool> RunAsync(IAnalysisAttempt<BriefDraftRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null) return await attempt.CompleteAsync(State.Blocked, BriefDraftErrorCodes.EvidenceUnavailable, cancellationToken);
        var selection = BriefSelection.Select(evidence.Records, sent => FitsWithRetryReserve(evidence, sent));
        var context = BriefDraftContext.Create(evidence, selection, officialNames);
        attempt.ContextJson = BriefDraftPrompt.SerializeContext(context);
        if (evidence.Records.IsEmpty && evidence.LeftOut > 0)
            return await attempt.CompleteAsync(State.Blocked, BriefDraftErrorCodes.NoCitableRecords, cancellationToken);
        if (evidence.Records.IsEmpty)
            return await SettleWithoutCallAsync(attempt, context.Fingerprint, BriefDraftErrorCodes.NoNewRecords, cancellationToken);
        if (selection.Sent.IsEmpty)
            return await attempt.CompleteAsync(State.Blocked, BriefDraftErrorCodes.OverByteCap, cancellationToken);
        if (evidence.Current?.Fingerprint == context.Fingerprint)
            return await SettleWithoutCallAsync(attempt, context.Fingerprint, BriefDraftErrorCodes.Unchanged, cancellationToken);
        return await DraftAsync(attempt, context, cancellationToken);
    }

    private async Task<bool> DraftAsync(IAnalysisAttempt<BriefDraftRun> attempt, BriefDraftContext context, CancellationToken cancellationToken)
    {
        var loop = new AnalysisValidationLoop<BriefDraftRun, BriefRevision>(Wrap, attempt, AnalysisRunOutcome.Drafted,
            BriefDraftTask.MaximumValidationPasses, errors => Prompt(context.Evidence, context.Selection.Sent, errors), model.DraftAsync,
            (json, run) => BriefDraftResolver.Resolve(json, context, run.RunId, run.FinishedAtUtc),
            async (runId, token) => (await store.GetRunAsync(runId, token))?.Run);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(Wrap(result.Run!),
                BriefDraftCheckpoint.From(attempt.Claim, result.Accepted!), cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null =>
                await attempt.CompleteAsync(State.Blocked, BriefDraftErrorCodes.OverByteCap, cancellationToken),
            AnalysisLoopEnd.Exhausted or AnalysisLoopEnd.NotSent => await loop.FailAsync(result, cancellationToken),
            _ => result.Progress
        };
    }

    private AnalysisModelRequest? Prompt(BriefDraftEvidence evidence, ImmutableArray<BriefSelectedRecord> sent,
        ImmutableArray<string> errors) =>
        BriefDraftPrompt.Build(evidence.Window, sent, officialNames, settings.PromptByteCap, errors);

    private bool FitsWithRetryReserve(BriefDraftEvidence evidence, ImmutableArray<BriefSelectedRecord> sent) =>
        BriefDraftPrompt.Build(evidence.Window, sent, officialNames, settings.PromptByteCap - BriefDraftTask.RetryReasonReserveBytes, default)
            is not null;

    private static Task<bool> SettleWithoutCallAsync(IAnalysisAttempt<BriefDraftRun> attempt, string inputHash, string code,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(BriefDraftTask.Model, inputHash, null);
        return attempt.SettleAsync(new BriefDraftRun(run, code), State.Succeeded, code, cancellationToken);
    }
}
