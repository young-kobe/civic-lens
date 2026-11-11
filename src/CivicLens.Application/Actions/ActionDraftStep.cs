using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Issues;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Actions;

public sealed class ActionDraftStep(IActionDraftStore store, ActionBillReader reader, IActionDraftModel model,
    ActionDraftSettings settings, IssueConfigurationRevision issues) : IAnalysisTaskStep<ActionDraftRun>
{
    private readonly IssueConfiguration issueConfiguration = issues.ReadConfiguration();

    public IAnalysisQueue<ActionDraftRun> Queue => store;

    public AnalysisSettings Settings => settings;

    /// <summary>True when a text draft asks for key terms. The text measure reports it, so a report says which prompt it measured.</summary>
    public bool DraftsKeyTerms => settings.DraftsKeyTerms;

    public AnalysisTaskIdentity Identity => ActionDraftTask.Identity;

    public ActionDraftRun Wrap(AnalysisRun run) => new(run, null);

    public async Task<bool> RunAsync(IAnalysisAttempt<ActionDraftRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, settings.DraftsFromBillText, cancellationToken);
        return evidence is null
            ? await attempt.CompleteAsync(State.Blocked, ActionDraftErrorCodes.EvidenceUnavailable, cancellationToken)
            : await DraftAsync(attempt, evidence, cancellationToken);
    }

    /// <summary>Runs the step on evidence that the caller read. The text measure calls this with the history of the bill cleared.</summary>
    public async Task<bool> DraftAsync(IAnalysisAttempt<ActionDraftRun> attempt, ActionDraftEvidence evidence, CancellationToken cancellationToken)
    {
        if (evidence.HasNewerCandidate)
            return await attempt.CompleteAsync(State.Succeeded, ActionDraftErrorCodes.SupersededByNewerCandidate, cancellationToken);
        if (evidence.SummaryPageCount > ActionDraftTask.MaximumSummaryPages) evidence = evidence with { Pages = [.. evidence.Pages.Where(page => page.Resource != ApiResource.BillSummaries)] };

        var read = await reader.ReadAsync(evidence, cancellationToken);
        if (read.Snapshot is not { } snapshot)
            return await attempt.CompleteAsync(State.Blocked, read.ErrorCode!, cancellationToken);
        var choice = BillIssueChoice.For(snapshot.Bill.PolicyArea, snapshot.Subjects, BillGate.DayOf(snapshot.Bill.UpdateDate), issueConfiguration);
        attempt.ContextJson = ActionDraftPrompt.SerializeContext(snapshot, choice, issues.Id);
        if (snapshot.Sponsorships.IsEmpty)
            return await SettleWithoutCallAsync(attempt, snapshot.Fingerprint, ActionDraftErrorCodes.NoTrackedOfficial, cancellationToken);
        if (snapshot.Sponsorships.Length > ActionDraftRevision.MaximumSponsorships)
            return await attempt.CompleteAsync(State.Blocked, ActionDraftErrorCodes.TooManySponsorships, cancellationToken);
        if (snapshot.Bill.Title.Length > ActionDraftRevision.MaximumTitleLength)
            return await attempt.CompleteAsync(State.Blocked, ActionDraftErrorCodes.TitleTooLong, cancellationToken);
        var policyAreaId = issueConfiguration.PolicyAreaIdFor(snapshot.Bill.PolicyArea);
        var context = new ActionDraftContext(evidence, snapshot, choice, policyAreaId, issues.Id);
        var change = context.ChangeSinceCurrent();
        if (evidence.IsRejected)
            return await SettleWithoutCallAsync(attempt, snapshot.Fingerprint, ActionDraftErrorCodes.Unchanged, cancellationToken,
                change is ActionChange.None or ActionChange.StaleOnly ? null : ActionDraftErrorCodes.RecordRejected);
        return change switch
        {
            ActionChange.None =>
                await SettleWithoutCallAsync(attempt, snapshot.Fingerprint, ActionDraftErrorCodes.Unchanged, cancellationToken),
            ActionChange.SponsorshipsOnly => await SettleRevisionWithoutCallAsync(attempt, context, ActionDraftErrorCodes.SponsorshipsChanged,
                ActionDraftResolver.CopyText, cancellationToken),
            _ => await DraftOrWriteAsync(attempt, context, change, cancellationToken)
        };
    }

    private async Task<bool> DraftOrWriteAsync(IAnalysisAttempt<ActionDraftRun> attempt, ActionDraftContext context, ActionChange change,
        CancellationToken cancellationToken)
    {
        var request = context.Snapshot.NewestSummary is null ? null : BuildTextRequest(context, default);
        if (request is not null) return await RunLoopAsync(attempt, context, TextLoop(attempt, context, request), cancellationToken);
        if (context.KeepsCurrentText)
            return change == ActionChange.StaleOnly
                ? await SettleWithoutCallAsync(attempt, context.Snapshot.Fingerprint, ActionDraftErrorCodes.Unchanged, cancellationToken)
                : await SettleRevisionWithoutCallAsync(attempt, context, ActionDraftErrorCodes.TextKept, ActionDraftResolver.CopyText, cancellationToken);
        if (context.Evidence.BillText is { } billText)
        {
            var keyTerms = settings.KeyTermGlossary is { } glossary ? ActionKeyTermScope.For(billText, glossary) : null;
            if (BuildBillTextRequest(context, billText, keyTerms, default) is { } billTextRequest)
                return await RunLoopAsync(attempt, context, BillTextLoop(attempt, context, billText, keyTerms, billTextRequest), cancellationToken);
        }
        return context.Issues.CandidateIds.IsEmpty
            ? await SettleRevisionWithoutCallAsync(attempt, context, ActionDraftErrorCodes.CodeWritten,
                (draft, createdAtUtc) => ActionDraftResolver.WriteByCode(draft, createdAtUtc), cancellationToken)
            : await RunLoopAsync(attempt, context, IssueLoop(attempt, context), cancellationToken);
    }

    private static Task<bool> SettleRevisionWithoutCallAsync(IAnalysisAttempt<ActionDraftRun> attempt, ActionDraftContext context,
        string reason, Func<ActionDraftContext, DateTimeOffset, ActionDraftRevision> write, CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(ActionDraftTask.Model, context.Snapshot.Fingerprint, null);
        var revision = write(context, run.FinishedAtUtc);
        return attempt.SettleAsync(new ActionDraftRun(run, reason),
            ActionDraftCheckpoint.From(attempt.Claim, revision, context.Evidence.ReviewStateVersion), cancellationToken);
    }

    private AnalysisModelRequest? BuildTextRequest(ActionDraftContext context, ImmutableArray<string> errors) =>
        context.Issues.BuildDroppingSubjectsWhenOverTheCap(choice => ActionDraftPrompt.Build(context.Snapshot, choice, settings.PromptByteCap, errors));

    private AnalysisValidationLoop<ActionDraftRun, ActionDraftRevision> TextLoop(IAnalysisAttempt<ActionDraftRun> attempt,
        ActionDraftContext context, AnalysisModelRequest firstRequest) =>
        new(Wrap, attempt, AnalysisRunOutcome.Drafted, ActionDraftTask.MaximumValidationPasses,
            errors => errors.IsDefaultOrEmpty ? firstRequest : BuildTextRequest(context, errors), model.DraftAsync,
            (json, run) => ActionDraftResolver.Resolve(json, context, run.RunId, run.FinishedAtUtc), ReadRunAsync);

    private AnalysisModelRequest? BuildBillTextRequest(ActionDraftContext context, ActionDraftBillText billText, ActionKeyTermScope? keyTerms,
        ImmutableArray<string> errors) =>
        context.Issues.BuildDroppingSubjectsWhenOverTheCap(
            choice => ActionTextPrompt.Build(context.Snapshot, billText, choice, settings.TextPromptByteCap, errors, keyTerms));

    private AnalysisValidationLoop<ActionDraftRun, ActionDraftRevision> BillTextLoop(IAnalysisAttempt<ActionDraftRun> attempt,
        ActionDraftContext context, ActionDraftBillText billText, ActionKeyTermScope? keyTerms, AnalysisModelRequest firstRequest) =>
        new(Wrap, attempt, AnalysisRunOutcome.Drafted, ActionDraftTask.MaximumValidationPasses,
            errors => errors.IsDefaultOrEmpty ? firstRequest : BuildBillTextRequest(context, billText, keyTerms, errors), model.DraftAsync,
            (json, run) => ActionDraftResolver.ResolveBillText(json, context, run.RunId, run.FinishedAtUtc, keyTerms), ReadRunAsync);

    private AnalysisValidationLoop<ActionDraftRun, ActionDraftRevision> IssueLoop(IAnalysisAttempt<ActionDraftRun> attempt,
        ActionDraftContext context) =>
        new(Wrap, attempt, AnalysisRunOutcome.Drafted, ActionDraftTask.MaximumValidationPasses,
            errors => context.Issues.BuildDroppingSubjectsWhenOverTheCap(
                choice => ActionIssuePrompt.Build(context.Snapshot, choice, settings.PromptByteCap, errors)), model.DraftAsync,
            (json, run) => ActionDraftResolver.ResolveIssueChoice(json, context, run.FinishedAtUtc), ReadRunAsync);

    private async Task<AnalysisRun?> ReadRunAsync(string runId, CancellationToken cancellationToken) =>
        (await store.GetRunAsync(runId, cancellationToken))?.Run;

    private async Task<bool> RunLoopAsync(IAnalysisAttempt<ActionDraftRun> attempt, ActionDraftContext context,
        AnalysisValidationLoop<ActionDraftRun, ActionDraftRevision> loop, CancellationToken cancellationToken)
    {
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(Wrap(result.Run!),
                ActionDraftCheckpoint.From(attempt.Claim, result.Accepted!, context.Evidence.ReviewStateVersion), cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null =>
                await attempt.CompleteAsync(State.Blocked, ActionDraftErrorCodes.OverByteCap, cancellationToken),
            AnalysisLoopEnd.Exhausted or AnalysisLoopEnd.NotSent => await loop.FailAsync(result, cancellationToken),
            _ => result.Progress
        };
    }

    private static Task<bool> SettleWithoutCallAsync(IAnalysisAttempt<ActionDraftRun> attempt, string inputHash, string code,
        CancellationToken cancellationToken, string? runReason = null)
    {
        var run = attempt.BypassRun(ActionDraftTask.Model, inputHash, null);
        return attempt.SettleAsync(new ActionDraftRun(run, runReason ?? code), State.Succeeded, code, cancellationToken);
    }
}
