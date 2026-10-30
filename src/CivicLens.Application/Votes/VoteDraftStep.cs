using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Analysis;
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
        attempt.ContextJson = VoteDraftPrompt.SerializeContext(snapshot);
        if (snapshot.Members.IsEmpty)
            return await SettleWithoutCallAsync(attempt, snapshot.Fingerprint, VoteDraftErrorCodes.NoTrackedMember, cancellationToken);
        if (snapshot.Members.Length > VoteDraftRevision.MaximumMembers)
            return await attempt.CompleteAsync(State.Blocked, VoteDraftErrorCodes.TooManyMembers, cancellationToken);
        if (snapshot.Facts.Length > VoteDraftRevision.MaximumFacts || snapshot.Facts.Any(fact => fact.Text.Length > VoteDraftRevision.MaximumFactLength))
            return await attempt.CompleteAsync(State.Blocked, VoteDraftErrorCodes.FactTooLong, cancellationToken);
        if (HasTooLongField(evidence, snapshot))
            return await attempt.CompleteAsync(State.Blocked, VoteDraftErrorCodes.FieldTooLong, cancellationToken);
        var choice = BillIssueChoice.For(snapshot.PolicyArea, snapshot.Day, issueConfiguration);
        var context = new VoteDraftContext(evidence, snapshot, choice, issues.Id);
        return context.ChangeSinceCurrent() switch
        {
            VoteChange.None => await SettleWithoutCallAsync(attempt, snapshot.Fingerprint, VoteDraftErrorCodes.Unchanged, cancellationToken),
            VoteChange.MemberVotesOnly => await CopyTextAsync(attempt, context, VoteDraftErrorCodes.MemberVotesChanged, cancellationToken),
            VoteChange.KindOnly => await CopyTextAsync(attempt, context, VoteDraftErrorCodes.KindChanged, cancellationToken),
            _ => await DraftAsync(attempt, context, cancellationToken)
        };
    }

    private static bool HasTooLongField(VoteDraftEvidence evidence, VoteSnapshot snapshot) =>
        evidence.Candidate.BillType?.Length > VoteDraftRevision.MaximumBillTypeLength ||
        snapshot.Members.Any(member => member.Cast.Length > VoteDraftRevision.MaximumCastLength);

    private Task<bool> CopyTextAsync(IAnalysisAttempt<VoteDraftRun> attempt, VoteDraftContext context, string reason,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(VoteDraftTask.Model, context.Snapshot.Fingerprint, null);
        var revision = VoteDraftResolver.CopyText(context, run.FinishedAtUtc);
        return attempt.SettleAsync(new VoteDraftRun(run, reason),
            VoteDraftCheckpoint.From(attempt.Claim, revision), cancellationToken);
    }

    private async Task<bool> DraftAsync(IAnalysisAttempt<VoteDraftRun> attempt, VoteDraftContext context, CancellationToken cancellationToken)
    {
        var loop = new AnalysisValidationLoop<VoteDraftRun, VoteDraftRevision>(Wrap, attempt, AnalysisRunOutcome.Drafted,
            VoteDraftTask.MaximumValidationPasses, errors => VoteDraftPrompt.Build(context.Snapshot, context.Issues, settings.PromptByteCap, errors),
            model.DraftAsync, (json, run) => VoteDraftResolver.Resolve(json, context, run.RunId, run.FinishedAtUtc),
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

    private static Task<bool> SettleWithoutCallAsync(IAnalysisAttempt<VoteDraftRun> attempt, string inputHash, string code,
        CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(VoteDraftTask.Model, inputHash, null);
        return attempt.SettleAsync(new VoteDraftRun(run, code), State.Succeeded, code, cancellationToken);
    }
}
