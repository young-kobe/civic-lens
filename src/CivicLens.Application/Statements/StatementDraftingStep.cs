using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Statements;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Statements;

public sealed class StatementDraftingStep(IStatementDraftStore store, IStatementDraftingModel model, StatementDraftSettings settings) : IAnalysisTaskStep<StatementDraftRun>
{
    private readonly StatementSpeakerRoster roster = new();

    public IAnalysisQueue<StatementDraftRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => StatementDraftTask.Identity;

    public StatementDraftRun Wrap(AnalysisRun run) => new(run, null);

    public async Task<bool> RunAsync(IAnalysisAttempt<StatementDraftRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null)
            return await attempt.CompleteAsync(State.Blocked, StatementDraftErrorCodes.EvidenceUnavailable, cancellationToken);
        if (evidence.ExistingDraftId is not null)
            return await attempt.CompleteAsync(State.Succeeded, StatementDraftErrorCodes.HumanDraftExists, cancellationToken);
        var officials = roster.OfficialsOn(evidence.ConfigurationRevision, evidence.Article.Extraction.SourceAttempt.SourceId, evidence.Article.FirstCapturedOn);
        if (officials.IsEmpty)
            return await attempt.CompleteAsync(State.Blocked, StatementDraftErrorCodes.SpeakersUnavailable, cancellationToken);
        attempt.ContextJson = StatementDraftPrompt.SerializeContext(officials, evidence.CarryOver);

        var loop = new AnalysisValidationLoop<StatementDraftRun, StatementDraftRevision>(this, attempt, AnalysisRunOutcome.Drafted,
            StatementDraftTask.MaximumValidationPasses, errors => StatementDraftPrompt.Build(evidence, officials, settings.PromptByteCap, errors),
            model.DraftAsync, (json, run) => Resolve(json, evidence, officials, run), async (runId, token) => (await store.GetRunAsync(runId, token))?.Run);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(Wrap(result.Run!),
                StatementDraftCheckpoint.From(attempt.Claim, State.Succeeded, revision: result.Accepted), cancellationToken),
            AnalysisLoopEnd.Exhausted => await attempt.SettleAsync(Wrap(result.Run!), State.Failed,
                AnalysisErrorCodes.ForOutcome(result.Run!.Outcome), cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null => await BypassAsync(attempt, evidence, cancellationToken),
            AnalysisLoopEnd.NotSent => await attempt.CompleteAsync(State.Failed, AnalysisErrorCodes.ForOutcome(result.Run!.Outcome),
                cancellationToken),
            _ => result.Progress
        };
    }

    private static AnalysisResolution<StatementDraftRevision> Resolve(string outputJson, StatementDraftEvidence evidence,
        ImmutableArray<AnalysisOfficial> officials, AnalysisRun run)
    {
        var resolution = StatementDraftResolver.Resolve(outputJson, evidence, officials, run.FinishedAtUtc);
        return resolution.Revision is { } revision ? AnalysisResolution<StatementDraftRevision>.Accept(revision)
            : AnalysisResolution<StatementDraftRevision>.Reject(resolution.Errors, resolution.HasCitationErrors);
    }

    private Task<bool> BypassAsync(IAnalysisAttempt<StatementDraftRun> attempt, StatementDraftEvidence evidence,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(StatementDraftTask.Model, evidence.Article.Extraction.TextSha256, null);
        return attempt.SettleAsync(new StatementDraftRun(run, StatementDraftErrorCodes.OverByteCap), State.Succeeded,
            StatementDraftErrorCodes.OverByteCap, cancellationToken);
    }
}
