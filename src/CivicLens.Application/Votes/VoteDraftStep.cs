using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Votes;

public sealed class VoteDraftStep(IVoteDraftStore store, VoteReader reader, IVoteDraftModel model, VoteDraftSettings settings,
    IssueConfigurationRevision issues) : IAnalysisTaskStep<VoteDraftRun>
{
    private readonly IssueConfiguration issueConfiguration = issues.ReadConfiguration();

    public IAnalysisQueue<VoteDraftRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => VoteDraftTask.Identity;

    public VoteDraftRun Wrap(AnalysisRun run) => new(run, null);

    public async Task<bool> RunAsync(IAnalysisAttempt<VoteDraftRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null) return await attempt.CompleteAsync(State.Blocked, VoteDraftErrorCodes.EvidenceUnavailable, cancellationToken);
        if (evidence.HasNewerCandidate)
            return await attempt.CompleteAsync(State.Succeeded, VoteDraftErrorCodes.SupersededByNewerCandidate, cancellationToken);

        var read = await reader.ReadAsync(evidence, cancellationToken);
        if (read.Snapshot is not { } snapshot)
            return await attempt.CompleteAsync(State.Blocked, read.ErrorCode!, cancellationToken);
        var choice = BillIssueChoice.For(snapshot.PolicyArea, snapshot.Subjects, snapshot.Day, issueConfiguration);
        attempt.ContextJson = VoteDraftPrompt.SerializeContext(snapshot, choice, issues.Id);
        if (snapshot.Members.IsEmpty)
            return await SettleWithoutCallAsync(attempt, snapshot.Fingerprint, VoteDraftErrorCodes.NoTrackedMember, cancellationToken);
        if (snapshot.Members.Length > VoteDraftRevision.MaximumMembers)
            return await attempt.CompleteAsync(State.Blocked, VoteDraftErrorCodes.TooManyMembers, cancellationToken);
        if (snapshot.Facts.Length > VoteDraftRevision.MaximumFacts || snapshot.Facts.Any(fact => fact.Text.Length > VoteDraftRevision.MaximumFactLength))
            return await attempt.CompleteAsync(State.Blocked, VoteDraftErrorCodes.FactTooLong, cancellationToken);
        if (HasTooLongField(evidence, snapshot))
            return await attempt.CompleteAsync(State.Blocked, VoteDraftErrorCodes.FieldTooLong, cancellationToken);
        if (!VoteFacts.HasResult(snapshot.Facts))
            return await attempt.CompleteAsync(State.Blocked, VoteDraftErrorCodes.NoResult, cancellationToken);
        var context = new VoteDraftContext(evidence, snapshot, choice, issues.Id);
        var change = context.ChangeSinceCurrent();
        if (evidence.IsRejected)
            return await SettleWithoutCallAsync(attempt, snapshot.Fingerprint, VoteDraftErrorCodes.Unchanged, cancellationToken,
                change is VoteChange.None or VoteChange.StaleOnly ? null : VoteDraftErrorCodes.RecordRejected);
        return change switch
        {
            VoteChange.None => await SettleWithoutCallAsync(attempt, snapshot.Fingerprint, VoteDraftErrorCodes.Unchanged, cancellationToken),
            VoteChange.MemberVotesOnly => await SettleRevisionWithoutCallAsync(attempt, context, VoteDraftErrorCodes.MemberVotesChanged,
                VoteDraftResolver.CopyText, cancellationToken),
            VoteChange.KindOnly => await SettleRevisionWithoutCallAsync(attempt, context, VoteDraftErrorCodes.KindChanged,
                evidence.Current!.TextOrigin == ReviewTextOrigin.Person ? VoteDraftResolver.CopyText : RewriteKeepingIssues, cancellationToken),
            _ => await DraftOrWriteAsync(attempt, context, change, cancellationToken)
        };
    }

    private static bool HasTooLongField(VoteDraftEvidence evidence, VoteSnapshot snapshot) =>
        evidence.Candidate.BillType?.Length > VoteDraftRevision.MaximumBillTypeLength ||
        snapshot.Members.Any(member => member.Cast.Length > VoteDraftRevision.MaximumCastLength);

    /// <summary>Code writes the wording. Text that a person wrote stays, with no call. A bill with candidate issues gets one issue-only call.
    /// A vote with no candidate issue gets no call.</summary>
    private async Task<bool> DraftOrWriteAsync(IAnalysisAttempt<VoteDraftRun> attempt, VoteDraftContext context, VoteChange change,
        CancellationToken cancellationToken)
    {
        if (context.Evidence.Current?.TextOrigin == ReviewTextOrigin.Person)
            return change == VoteChange.StaleOnly
                ? await SettleWithoutCallAsync(attempt, context.Snapshot.Fingerprint, VoteDraftErrorCodes.Unchanged, cancellationToken)
                : await SettleRevisionWithoutCallAsync(attempt, context, VoteDraftErrorCodes.TextKept, VoteDraftResolver.CopyText, cancellationToken);
        if (context.Issues.CandidateIds.IsEmpty)
            return await SettleRevisionWithoutCallAsync(attempt, context, VoteDraftErrorCodes.CodeWritten,
                (draft, createdAtUtc) => VoteDraftResolver.WriteByCode(draft, createdAtUtc), cancellationToken);
        var loop = new AnalysisValidationLoop<VoteDraftRun, VoteDraftRevision>(Wrap, attempt, AnalysisRunOutcome.Drafted,
            AiTextRules.MaximumValidationPasses, errors => context.Issues.BuildDroppingSubjectsWhenOverTheCap(issues => VoteDraftPrompt.Build(context.Snapshot, issues, settings.PromptByteCap, errors)),
            model.DraftAsync, (json, run) => VoteDraftResolver.ResolveIssueChoice(json, context, run.FinishedAtUtc),
            async (runId, token) => (await store.GetRunAsync(runId, token))?.Run);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(Wrap(result.Run!),
                VoteDraftCheckpoint.From(attempt.Claim, result.Accepted!), cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null =>
                await attempt.CompleteAsync(State.Blocked, VoteDraftErrorCodes.OverByteCap, cancellationToken),
            AnalysisLoopEnd.Exhausted or AnalysisLoopEnd.NotSent => await loop.FailAsync(result, cancellationToken),
            _ => result.Progress
        };
    }

    private static VoteDraftRevision RewriteKeepingIssues(VoteDraftContext context, DateTimeOffset createdAtUtc) =>
        VoteDraftResolver.WriteByCode(context, createdAtUtc, context.Evidence.Current!.TrackedIssueIds);

    private static Task<bool> SettleRevisionWithoutCallAsync(IAnalysisAttempt<VoteDraftRun> attempt, VoteDraftContext context, string reason,
        Func<VoteDraftContext, DateTimeOffset, VoteDraftRevision> write, CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(VoteDraftTask.Model, context.Snapshot.Fingerprint, null);
        var revision = write(context, run.FinishedAtUtc);
        return attempt.SettleAsync(new VoteDraftRun(run, reason),
            VoteDraftCheckpoint.From(attempt.Claim, revision), cancellationToken);
    }

    private static Task<bool> SettleWithoutCallAsync(IAnalysisAttempt<VoteDraftRun> attempt, string inputHash, string code,
        CancellationToken cancellationToken, string? runReason = null)
    {
        var run = attempt.BypassRun(VoteDraftTask.Model, inputHash, null);
        return attempt.SettleAsync(new VoteDraftRun(run, runReason ?? code), State.Succeeded, code, cancellationToken);
    }
}
