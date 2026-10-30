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

    public AnalysisTaskIdentity Identity => ActionDraftTask.Identity;

    public ActionDraftRun Wrap(AnalysisRun run) => new(run, null);

    public async Task<bool> RunAsync(IAnalysisAttempt<ActionDraftRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null) return await attempt.CompleteAsync(State.Blocked, ActionDraftErrorCodes.EvidenceUnavailable, cancellationToken);
        if (evidence.HasNewerCandidate)
            return await attempt.CompleteAsync(State.Succeeded, ActionDraftErrorCodes.SupersededByNewerCandidate, cancellationToken);
        var summariesOmitted = evidence.SummaryPageCount > ActionDraftTask.MaximumSummaryPages;
        if (summariesOmitted) evidence = evidence with { Pages = [.. evidence.Pages.Where(page => page.Resource != ApiResource.BillSummaries)] };

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
        return context.ChangeSinceCurrent() switch
        {
            ActionChange.None =>
                await SettleWithoutCallAsync(attempt, snapshot.Fingerprint, ActionDraftErrorCodes.Unchanged, cancellationToken),
            ActionChange.SponsorshipsOnly => await CopyTextAsync(attempt, context, cancellationToken),
            _ => await DraftAsync(attempt, context, summariesOmitted, cancellationToken)
        };
    }

    private Task<bool> CopyTextAsync(IAnalysisAttempt<ActionDraftRun> attempt, ActionDraftContext context,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(ActionDraftTask.Model, context.Snapshot.Fingerprint, null);
        var revision = ActionDraftResolver.CopyText(context, run.FinishedAtUtc);
        return attempt.SettleAsync(new ActionDraftRun(run, ActionDraftErrorCodes.SponsorshipsChanged),
            ActionDraftCheckpoint.From(attempt.Claim, revision), cancellationToken);
    }

    private async Task<bool> DraftAsync(IAnalysisAttempt<ActionDraftRun> attempt, ActionDraftContext context,
        bool summariesOmitted, CancellationToken cancellationToken)
    {
        var snapshot = context.Snapshot;
        var summaryOmittedInputHashes = new HashSet<string>(StringComparer.Ordinal);
        AnalysisModelRequest? BuildRequest(ImmutableArray<string> errors)
        {
            if (ActionDraftPrompt.Build(snapshot, context.Issues, settings.PromptByteCap, errors) is { } request) return request;
            if (context.Issues.HasSubjects &&
                ActionDraftPrompt.Build(snapshot, context.Issues.WithoutSubjects(), settings.PromptByteCap, errors) is { } withoutSubjects)
                return withoutSubjects;
            var withoutSummary = ActionDraftPrompt.Build(snapshot, context.Issues.WithoutSubjects(), settings.PromptByteCap, errors, includeSummary: false);
            if (withoutSummary is not null) summaryOmittedInputHashes.Add(withoutSummary.InputHash);
            return withoutSummary;
        }
        ActionDraftRun WrapWithReason(AnalysisRun run) =>
            new(run, summariesOmitted || summaryOmittedInputHashes.Contains(run.InputHash) ? ActionDraftErrorCodes.SummaryOmitted : null);

        var loop = new AnalysisValidationLoop<ActionDraftRun, ActionDraftRevision>(WrapWithReason, attempt, AnalysisRunOutcome.Drafted,
            ActionDraftTask.MaximumValidationPasses, BuildRequest, model.DraftAsync,
            (json, run) => ActionDraftResolver.Resolve(json, context, run.RunId, run.FinishedAtUtc),
            async (runId, token) => (await store.GetRunAsync(runId, token))?.Run);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(WrapWithReason(result.Run!),
                ActionDraftCheckpoint.From(attempt.Claim, result.Accepted!), cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null =>
                await attempt.CompleteAsync(State.Blocked, ActionDraftErrorCodes.OverByteCap, cancellationToken),
            AnalysisLoopEnd.Exhausted or AnalysisLoopEnd.NotSent => await loop.FailAsync(result, cancellationToken),
            _ => result.Progress
        };
    }

    private static Task<bool> SettleWithoutCallAsync(IAnalysisAttempt<ActionDraftRun> attempt, string inputHash, string code,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(ActionDraftTask.Model, inputHash, null);
        return attempt.SettleAsync(new ActionDraftRun(run, code), State.Succeeded, code, cancellationToken);
    }
}
