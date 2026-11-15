using CivicLens.Application.Issues;
using CivicLens.Application.Review;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

/// <summary>
/// Moves published votes from model text to code text, with no model call. For each published vote whose current revision holds model text,
/// it appends a code revision with the same facts and the same issues, so code approves it and the next release publishes it.
/// The revision cites a Bypassed <c>codeWritten</c> run, as the worker does. It checks the newest candidate of the roll call, because an unchanged
/// update writes no revision. A vote whose newest candidate changed the facts, or waits in the queue, is left to the worker.
/// A moved vote holds code text, so a second run finds nothing.
/// </summary>
public sealed class BackfillVoteCodeText(IVoteDraftStore store, VoteReader reader, IssueConfigurationRevision issues, TimeProvider clock)
{
    private readonly IssueConfiguration issueConfiguration = issues.ReadConfiguration();

    public async Task<VoteCodeTextBackfillResult> ExecuteAsync(ReviewActor actor, int limit, string? afterRecordId,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var targets = await store.ListPublishedModelTextAsync(limit, afterRecordId, cancellationToken);
        var skips = new List<VoteCodeTextSkip>();
        foreach (var target in targets)
            if (await MoveAsync(target, cancellationToken) is { } reason) skips.Add(new(target.RecordId, reason));
        var next = targets.Count == limit ? targets[^1].RecordId : null;
        return new(targets.Count, targets.Count - skips.Count, skips.Count, skips, next);
    }

    /// <summary>Returns null when the vote moved, else the reason it kept its model text.</summary>
    private async Task<string?> MoveAsync(VoteCodeTextTarget target, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(target.CandidateId, cancellationToken);
        if (evidence is null) return VoteDraftErrorCodes.EvidenceUnavailable;
        // The list names the newest candidate, so the facts and queue checks below apply to it. A candidate that arrives after the list supersedes it.
        if (evidence.HasNewerCandidate) return VoteDraftErrorCodes.SupersededByNewerCandidate;
        if (evidence.Current is not { TextOrigin: ReviewTextOrigin.Model } current) return VoteCodeTextSkip.NotModelText;
        var read = await reader.ReadAsync(evidence, cancellationToken);
        if (read.Snapshot is not { } snapshot) return read.ErrorCode;
        var choice = BillIssueChoice.For(snapshot.PolicyArea, snapshot.Subjects, snapshot.Day, issueConfiguration);
        var context = new VoteDraftContext(evidence, snapshot, choice, issues.Id);
        // Only the wording changes. A change of facts, members, kind, or issue candidates needs the worker, which owns those rules.
        if (context.ChangeSinceCurrent() is not (VoteChange.None or VoteChange.StaleOnly)) return VoteCodeTextSkip.FactsChanged;
        var now = clock.GetUtcNow();
        var run = new AnalysisRun(Guid.NewGuid().ToString("N"), target.CandidateId, VoteDraftTask.Task, VoteDraftTask.TaskVersion, VoteDraftTask.Model,
            VoteDraftTask.PromptVersion, VoteDraftTask.SchemaVersion, snapshot.Fingerprint, null, AnalysisRunOutcome.Bypassed, AnalysisTokenUsage.None, 0,
            null, null, null, [], VoteDraftPrompt.SerializeContext(snapshot, choice, issues.Id), now, now);
        var revision = VoteDraftResolver.WriteByCode(context, now, current.TrackedIssueIds);
        return await store.AppendCodeTextAsync(revision, new VoteDraftRun(run, VoteDraftErrorCodes.CodeWritten), cancellationToken);
    }
}
