using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

/// <summary>
/// Drafts the text of fixed roll calls through the real step, and keeps nothing. It reads evidence and sends model calls only. It clears the draft
/// history of each roll call, so a roll call with a current record is drafted again as a new one. It follows <see cref="ActionTextMeasure"/>:
/// the same spend cap rule, the same stop on a model failure that needs a person, and the same <see cref="AnalysisMeasureAttempt{TRun, TRevision}"/>.
/// </summary>
public sealed class VoteDecidesMeasure(IVoteDraftStore store, VoteDraftStep step)
{
    public async Task<VoteDecidesMeasureReport> RunAsync(IReadOnlyList<VoteMeasureRoll> rolls, long tokenCap, CancellationToken cancellationToken)
    {
        if (tokenCap < 1) throw new ArgumentOutOfRangeException(nameof(tokenCap), tokenCap, "The spend cap must be at least 1 token.");
        var results = ImmutableArray.CreateBuilder<VoteDecidesMeasureVote>();
        var spent = 0L;
        try
        {
            foreach (var roll in rolls)
            {
                var result = results.Any(item => item.EndsRun) ? NotRun(roll, results) : await MeasureAsync(roll, tokenCap, spent, cancellationToken);
                spent += result.ChargedTokens;
                results.Add(result);
            }
        }
        catch (OperationCanceledException exception)
        {
            throw new VoteDecidesMeasureCanceledException(ReportOf(tokenCap, results), exception);
        }
        return ReportOf(tokenCap, results);
    }

    private static VoteDecidesMeasureReport ReportOf(long tokenCap, ImmutableArray<VoteDecidesMeasureVote>.Builder results) =>
        new(VoteDraftTask.PromptVersion, VoteDraftTask.SchemaVersion, VoteDraftTask.Model, tokenCap, results.ToImmutable());

    private static VoteDecidesMeasureVote NotRun(VoteMeasureRoll roll, IEnumerable<VoteDecidesMeasureVote> earlier) =>
        earlier.Any(item => item.Outcome == AnalysisMeasureOutcome.StoppedByModel)
            ? VoteDecidesMeasureVote.WithoutCall(roll.Id, AnalysisMeasureOutcome.StoppedByModel, VoteDecidesMeasureReason.ModelFailure)
            : VoteDecidesMeasureVote.WithoutCall(roll.Id, AnalysisMeasureOutcome.StoppedByCap, VoteDecidesMeasureReason.SpendCap);

    private async Task<VoteDecidesMeasureVote> MeasureAsync(VoteMeasureRoll roll, long tokenCap, long spent, CancellationToken cancellationToken)
    {
        if (await store.NewestCandidateIdAsync(roll, cancellationToken) is not { } candidateId)
            return VoteDecidesMeasureVote.Skipped(roll.Id, VoteDecidesMeasureReason.NoCandidate);
        var evidence = await store.ReadEvidenceAsync(candidateId, cancellationToken);
        if (evidence is null) return VoteDecidesMeasureVote.Skipped(roll.Id, VoteDraftErrorCodes.EvidenceUnavailable);
        if (evidence.BillSummaries.Count == 0) return VoteDecidesMeasureVote.Skipped(roll.Id, VoteDecidesMeasureReason.NoBillSummary);

        var attempt = new AnalysisMeasureAttempt<VoteDraftRun, VoteDraftRevision>(candidateId, tokenCap, spent, VoteDraftTask.Identity, run => run.Run,
            checkpoint => (checkpoint as VoteDraftCheckpoint)?.Revision);
        await step.ProcessAsync(attempt, evidence with { Current = null, HasStaleDraft = false, IsRejected = false }, cancellationToken);
        return Summarize(roll, attempt);
    }

    private static VoteDecidesMeasureVote Summarize(VoteMeasureRoll roll, AnalysisMeasureAttempt<VoteDraftRun, VoteDraftRevision> attempt)
    {
        var usage = attempt.Runs.Aggregate(AnalysisTokenUsage.None, (sum, run) => new(sum.InputTokens + run.Usage.InputTokens,
            sum.OutputTokens + run.Usage.OutputTokens, sum.CacheReadTokens + run.Usage.CacheReadTokens, sum.CacheWriteTokens + run.Usage.CacheWriteTokens));
        VoteDecidesMeasureVote With(AnalysisMeasureOutcome outcome, string? reason, VoteDecides? decides = null) =>
            new(roll.Id, outcome, reason, ModeOf(attempt), attempt.Calls, usage, attempt.Runs.Sum(run => run.ChargedTokens), decides?.Text, decides?.Quote,
                decides is null ? null : ReadingGrade.Rounded(decides.Text), [.. attempt.Runs.SelectMany(run => run.ValidationErrors)],
                [.. attempt.Runs.Where(run => run.OutputJson is not null).Select(run => new AnalysisMeasurePass(run.Outcome, run.OutputJson))]);

        if (attempt.Failure is { } failure)
            return With(AnalysisFailureRules.NeedsPerson(failure) ? AnalysisMeasureOutcome.StoppedByModel : AnalysisMeasureOutcome.Failed, attempt.ErrorCode);
        if (attempt.StoppedByCap) return With(AnalysisMeasureOutcome.StoppedByCap, VoteDecidesMeasureReason.SpendCap);
        if (attempt.Revision is { Decides: { } decides }) return With(AnalysisMeasureOutcome.Accepted, null, decides);
        if (attempt.Calls == 0) return With(AnalysisMeasureOutcome.Skipped, attempt.ErrorCode);
        return With(IsResolverRefusal(attempt.Runs[^1].Outcome) ? AnalysisMeasureOutcome.Rejected : AnalysisMeasureOutcome.Failed, attempt.ErrorCode);
    }

    private static string? ModeOf(AnalysisMeasureAttempt<VoteDraftRun, VoteDraftRevision> attempt)
    {
        if (attempt.ContextJson is null) return null;
        using var context = JsonDocument.Parse(attempt.ContextJson);
        return context.RootElement.TryGetProperty("decidesMode", out var mode) ? mode.GetString() : null;
    }

    /// <summary>Only a run that code refused is Rejected. A run that the model stopped, such as an answer cut at the token limit or a refusal, is Failed.</summary>
    private static bool IsResolverRefusal(AnalysisRunOutcome outcome) => outcome is AnalysisRunOutcome.OutputRejected or AnalysisRunOutcome.CitationRejected;
}
