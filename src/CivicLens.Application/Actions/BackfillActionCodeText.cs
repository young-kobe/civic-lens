using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Issues;
using CivicLens.Application.Review;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

/// <summary>
/// Moves published bill records from retired text to code text, with no model call. The retired text is model text that no bill text backs
/// (a CRS rewrite or a title-only draft), and a code headline that names the sponsor instead of the title. For each such record it appends
/// a code revision with the same facts and the same issues, so code approves it and the next release publishes it. The revision cites a
/// Bypassed <c>codeWritten</c> run, as the worker does. A record whose facts changed is left to the worker. A moved record holds current code
/// text, so a second run finds nothing. The precedent is <c>BackfillVoteCodeText</c> of the votes code text change.
/// </summary>
public sealed class BackfillActionCodeText(IActionDraftStore store, ActionBillReader reader, IssueConfigurationRevision issues, TimeProvider clock)
{
    private readonly IssueConfiguration issueConfiguration = issues.ReadConfiguration();

    public async Task<ActionCodeTextBackfillResult> ExecuteAsync(ReviewActor actor, int limit, string? afterRecordId,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var targets = await store.ListPublishedRetiredTextAsync(limit, afterRecordId, cancellationToken);
        var skips = new List<ActionCodeTextSkip>();
        foreach (var target in targets)
            if (await MoveAsync(target, cancellationToken) is { } reason) skips.Add(new(target.RecordId, reason));
        var next = targets.Count == limit ? targets[^1].RecordId : null;
        return new(targets.Count, targets.Count - skips.Count, skips.Count, skips, next);
    }

    /// <summary>Returns null when the record moved, else the reason it kept its text.</summary>
    private async Task<string?> MoveAsync(ActionCodeTextTarget target, CancellationToken cancellationToken)
    {
        // The bill text is not read: a retired draft cites none, and the worker drafts from bill text when the gate names new text.
        var evidence = await store.ReadEvidenceAsync(target.CandidateId, withBillText: false, cancellationToken);
        if (evidence is null) return ActionDraftErrorCodes.EvidenceUnavailable;
        if (evidence.HasNewerCandidate) return ActionDraftErrorCodes.SupersededByNewerCandidate;
        if (evidence.IsRejected) return ActionDraftErrorCodes.RecordRejected;
        if (evidence.Current is not { TextOrigin: ReviewTextOrigin.Model or ReviewTextOrigin.Code } current) return ActionCodeTextSkip.NotRetiredText;
        evidence = evidence.WithReadableSummaries();
        var read = await reader.ReadAsync(evidence, cancellationToken);
        if (read.Snapshot is not { } snapshot) return read.ErrorCode;
        var choice = BillIssueChoice.For(snapshot.Bill.PolicyArea, snapshot.Subjects, BillGate.DayOf(snapshot.Bill.UpdateDate), issueConfiguration);
        var context = new ActionDraftContext(evidence, snapshot, choice, issueConfiguration.PolicyAreaIdFor(snapshot.Bill.PolicyArea), issues.Id);
        // Only the wording changes. A change of facts, sponsors, or issue candidates needs the worker, which owns those rules.
        if (context.ChangeSinceCurrent() is not (ActionChange.None or ActionChange.StaleOnly)) return ActionCodeTextSkip.FactsChanged;
        // The worker keeps model text while Congress.gov counts a summary that this candidate cannot read, so the command keeps it too.
        if (snapshot.NewestSummary is null && context.KeepsCurrentText) return ActionDraftErrorCodes.TextKept;
        var now = clock.GetUtcNow();
        var run = new AnalysisRun(Guid.NewGuid().ToString("N"), target.CandidateId, ActionDraftTask.Task, ActionDraftTask.TaskVersion, ActionDraftTask.Model,
            ActionDraftTask.PromptVersion, ActionDraftTask.SchemaVersion, snapshot.Fingerprint, null, AnalysisRunOutcome.Bypassed, AnalysisTokenUsage.None, 0,
            null, null, null, [], ActionDraftPrompt.SerializeContext(snapshot, choice, issues.Id), now, now);
        var revision = ActionDraftResolver.WriteByCode(context, now, current.TrackedIssueIds);
        return await store.AppendCodeTextAsync(revision, new ActionDraftRun(run, ActionDraftErrorCodes.CodeWritten), evidence.ReviewStateVersion,
            cancellationToken);
    }
}
