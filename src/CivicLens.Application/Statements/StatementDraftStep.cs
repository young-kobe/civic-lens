using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Statements;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Statements;

public sealed class StatementDraftStep(IStatementDraftStore store, IStatementDraftModel model, StatementDraftSettings settings,
    StatementSelectionResolver selections) : IAnalysisTaskStep<StatementDraftRun>
{
    public IAnalysisQueue<StatementDraftRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => StatementDraftTask.Identity;

    public StatementDraftRun Wrap(AnalysisRun run) => new(run, null);

    public async Task<bool> RunAsync(IAnalysisAttempt<StatementDraftRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null)
            return await attempt.CompleteAsync(State.Blocked, StatementDraftErrorCodes.EvidenceUnavailable, cancellationToken);
        if (evidence.ExistingDraft is { IsStaleModelDraft: false })
            return await attempt.CompleteAsync(State.Succeeded, StatementDraftErrorCodes.HumanDraftExists, cancellationToken);
        var (allowed, officials, officeOf) = selections.Resolve(evidence.ConfigurationRevision, evidence.Article.Extraction.SourceAttempt.SourceId,
            evidence.Article.FirstCapturedOn);
        if (officials.IsEmpty)
            return await attempt.CompleteAsync(State.Blocked, StatementDraftErrorCodes.SpeakersUnavailable, cancellationToken);
        attempt.ContextJson = StatementDraftPrompt.SerializeContext(officials, evidence.CarryOver);
        if (StatementSourceText.Read(evidence.Article.Extraction) is { } sourceText)
            return await SettleSourceTextAsync(attempt, evidence, sourceText, allowed, SpeakersOf(officials, officeOf), officeOf, cancellationToken);

        var loop = new AnalysisValidationLoop<StatementDraftRun, StatementDraftRevision>(Wrap, attempt, AnalysisRunOutcome.Drafted,
            AiTextRules.MaximumValidationPasses, errors => StatementDraftPrompt.Build(evidence, officials, settings.PromptByteCap, errors),
            model.DraftAsync, (json, run) => Resolve(json, evidence, officials, run), async (runId, token) => (await store.GetRunAsync(runId, token))?.Run);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(Wrap(result.Run!), StatementDraftCheckpoint.From(attempt.Claim,
                result.Accepted!, StatementCodeApproval.Check(evidence, allowed, officeOf, result.Accepted!)), cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null => await BypassAsync(attempt, evidence, cancellationToken),
            AnalysisLoopEnd.Exhausted or AnalysisLoopEnd.NotSent => await loop.FailAsync(result, cancellationToken),
            _ => result.Progress
        };
    }

    /// <summary>A short release makes no model call. Code writes its source text as the revision, with a Bypassed run, and the code
    /// approval checks it as it checks a model draft. This follows the code text of an action.</summary>
    private static Task<bool> SettleSourceTextAsync(IAnalysisAttempt<StatementDraftRun> attempt, StatementDraftEvidence evidence,
        StatementSourceText sourceText, StatementAllowedSelections allowed, ImmutableArray<string> speakers, string? officeOf,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(StatementDraftTask.Model, evidence.Article.Extraction.TextSha256, null);
        var revision = sourceText.Revision(evidence, speakers, run.FinishedAtUtc);
        return attempt.SettleAsync(new StatementDraftRun(run, StatementDraftErrorCodes.SourceText), StatementDraftCheckpoint.From(attempt.Claim,
            revision, StatementCodeApproval.Check(evidence, allowed, officeOf, revision)), cancellationToken);
    }

    /// <summary>Code knows the speaker only from the office owner of the source or from its one covered official.</summary>
    private static ImmutableArray<string> SpeakersOf(ImmutableArray<AnalysisOfficial> officials, string? officeOf) =>
        officials.Any(official => official.Id == officeOf) ? [officeOf!] : officials is [var only] ? [only.Id] : [];

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
