using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Relationships;

public sealed class RelationshipDraftStep(IRelationshipDraftStore store, IRelationshipDraftModel model, RelationshipDraftSettings settings,
    IReadOnlyDictionary<string, string> officialNames) : IAnalysisTaskStep<RelationshipDraftRun>
{
    public IAnalysisQueue<RelationshipDraftRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => RelationshipDraftTask.Identity;

    public RelationshipDraftRun Wrap(AnalysisRun run) => new(run, null);

    public async Task<bool> RunAsync(IAnalysisAttempt<RelationshipDraftRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null) return await attempt.CompleteAsync(State.Blocked, RelationshipDraftErrorCodes.EvidenceUnavailable, cancellationToken);
        if (!evidence.SidesAreApproved) return await attempt.CompleteAsync(State.Blocked, RelationshipDraftErrorCodes.SideNotApproved, cancellationToken);
        if (evidence.FindPair() is not { } pair)
        {
            attempt.ContextJson = RelationshipDraftPrompt.SerializeUnmatchedContext(evidence.PairId);
            return await SettleWithoutCallAsync(attempt, evidence.PairId, RelationshipDraftErrorCodes.NoLongerMatches, cancellationToken);
        }
        var context = RelationshipDraftContext.Create(evidence, pair, officialNames);
        attempt.ContextJson = RelationshipDraftPrompt.SerializeContext(context);
        if (KeepsPersonRevision(evidence, pair)) return await SettleUnchangedAsync(attempt, context, cancellationToken);
        if (evidence.SettledFingerprint != context.Fingerprint) return await DraftAsync(attempt, context, cancellationToken);
        return evidence.Current switch
        {
            null => await SettleUnchangedAsync(attempt, context, cancellationToken),
            { } current when current.Target == pair.Target.Citation => await SettleUnchangedAsync(attempt, context, cancellationToken),
            { } current when ReviewAuthor.KindOf(current.AuthorSubject) == ReviewAuthorKind.Analysis =>
                await ReciteAsync(attempt, context, cancellationToken),
            _ => await DraftAsync(attempt, context, cancellationToken)
        };
    }

    private static Task<bool> SettleUnchangedAsync(IAnalysisAttempt<RelationshipDraftRun> attempt, RelationshipDraftContext context,
        CancellationToken cancellationToken) =>
        SettleWithoutCallAsync(attempt, context.Fingerprint, RelationshipDraftErrorCodes.Unchanged, cancellationToken);

    private static Task<bool> ReciteAsync(IAnalysisAttempt<RelationshipDraftRun> attempt, RelationshipDraftContext context,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(RelationshipDraftTask.Model, context.Fingerprint, null);
        var revision = RelationshipDraftResolver.Recite(context, run.FinishedAtUtc);
        return attempt.SettleAsync(new RelationshipDraftRun(run, RelationshipDraftErrorCodes.Unchanged),
            RelationshipDraftCheckpoint.From(attempt.Claim, revision), cancellationToken);
    }

    private static bool KeepsPersonRevision(RelationshipDraftEvidence evidence, RelationshipPair pair) =>
        evidence.Current?.IsPersonRevisionCiting(
            new(RelationshipRecordKind.Statement, pair.Statement.RecordId, pair.Statement.Revision.RevisionNumber), pair.Target.Citation) == true;

    private async Task<bool> DraftAsync(IAnalysisAttempt<RelationshipDraftRun> attempt, RelationshipDraftContext context,
        CancellationToken cancellationToken)
    {
        var loop = new AnalysisValidationLoop<RelationshipDraftRun, RelationshipRevision?>(Wrap, attempt, AnalysisRunOutcome.Drafted,
            RelationshipDraftTask.MaximumValidationPasses,
            errors => RelationshipDraftPrompt.Build(context, officialNames, settings.PromptByteCap, errors), model.DraftAsync,
            (json, run) => RelationshipDraftResolver.Resolve(json, context, run.FinishedAtUtc),
            async (runId, token) => (await store.GetRunAsync(runId, token))?.Run);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await SettleAcceptedAsync(attempt, result.Run!, result.Accepted, cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null =>
                await attempt.CompleteAsync(State.Blocked, RelationshipDraftErrorCodes.OverByteCap, cancellationToken),
            AnalysisLoopEnd.Exhausted or AnalysisLoopEnd.NotSent => await loop.FailAsync(result, cancellationToken),
            _ => result.Progress
        };
    }

    private Task<bool> SettleAcceptedAsync(IAnalysisAttempt<RelationshipDraftRun> attempt, AnalysisRun run, RelationshipRevision? revision,
        CancellationToken cancellationToken) =>
        revision is null
            ? attempt.SettleAsync(new RelationshipDraftRun(run, RelationshipDraftErrorCodes.NoRelationship), State.Succeeded,
                RelationshipDraftErrorCodes.NoRelationship, cancellationToken)
            : attempt.SettleAsync(Wrap(run), RelationshipDraftCheckpoint.From(attempt.Claim, revision), cancellationToken);

    private static Task<bool> SettleWithoutCallAsync(IAnalysisAttempt<RelationshipDraftRun> attempt, string inputHash, string code,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(RelationshipDraftTask.Model, inputHash, null);
        return attempt.SettleAsync(new RelationshipDraftRun(run, code), State.Succeeded, code, cancellationToken);
    }
}
