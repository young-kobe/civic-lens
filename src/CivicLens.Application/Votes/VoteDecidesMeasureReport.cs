using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Votes;

/// <summary>One measured roll call. <see cref="Mode"/> is Content or Procedure, and it is null when the step asked no decides text. <see cref="Decides"/> is the accepted text and its quote, and <see cref="Grade"/> is its reading grade.</summary>
public sealed record VoteDecidesMeasureVote(string VoteId, AnalysisMeasureOutcome Outcome, string? Reason, string? Mode, int Calls,
    AnalysisTokenUsage Usage, long ChargedTokens, string? Decides, string? Quote, double? Grade, ImmutableArray<string> Rejections,
    ImmutableArray<AnalysisMeasurePass> Passes)
{
    public bool EndsRun => Outcome is AnalysisMeasureOutcome.StoppedByCap or AnalysisMeasureOutcome.StoppedByModel;

    public static VoteDecidesMeasureVote Skipped(string voteId, string reason) => WithoutCall(voteId, AnalysisMeasureOutcome.Skipped, reason);

    public static VoteDecidesMeasureVote WithoutCall(string voteId, AnalysisMeasureOutcome outcome, string reason) =>
        new(voteId, outcome, reason, null, 0, AnalysisTokenUsage.None, 0, null, null, null, [], []);
}

public sealed record VoteDecidesMeasureTotals(int Accepted, int Rejected, int Failed, int Skipped, int StoppedByCap, int StoppedByModel, int Calls,
    AnalysisTokenUsage Usage, long ChargedTokens, double? MeanGrade, int ContentVotes, int ProcedureVotes);

/// <summary>The result of one measure run. The totals count the roll calls of the file and the calls and tokens of the run.</summary>
public sealed record VoteDecidesMeasureReport(string PromptVersion, string SchemaVersion, string Model, long TokenCap,
    ImmutableArray<VoteDecidesMeasureVote> Votes)
{
    public VoteDecidesMeasureTotals Totals => new(Count(AnalysisMeasureOutcome.Accepted), Count(AnalysisMeasureOutcome.Rejected),
        Count(AnalysisMeasureOutcome.Failed), Count(AnalysisMeasureOutcome.Skipped), Count(AnalysisMeasureOutcome.StoppedByCap),
        Count(AnalysisMeasureOutcome.StoppedByModel), Votes.Sum(vote => vote.Calls), TotalUsage(), Votes.Sum(vote => vote.ChargedTokens), MeanGrade(),
        CountMode(nameof(VoteDecidesMode.Content)), CountMode(nameof(VoteDecidesMode.Procedure)));

    private int Count(AnalysisMeasureOutcome outcome) => Votes.Count(vote => vote.Outcome == outcome);

    /// <summary>The roll calls that asked for a decides text in this mode, whatever the outcome. The owner reads this to see the mix of passage and procedure votes.</summary>
    private int CountMode(string mode) => Votes.Count(vote => vote.Mode == mode);

    private double? MeanGrade() => Votes.Select(vote => vote.Grade).OfType<double>().ToArray() is { Length: > 0 } grades ? Math.Round(grades.Average(), 1) : null;

    private AnalysisTokenUsage TotalUsage() => Votes.Aggregate(AnalysisTokenUsage.None, (sum, vote) => new(sum.InputTokens + vote.Usage.InputTokens,
        sum.OutputTokens + vote.Usage.OutputTokens, sum.CacheReadTokens + vote.Usage.CacheReadTokens, sum.CacheWriteTokens + vote.Usage.CacheWriteTokens));
}

public static class VoteDecidesMeasureReason
{
    public const string NoCandidate = "no-candidate";
    public const string NoBillSummary = "no-bill-summary";
    public const string SpendCap = "spend-cap";
    public const string ModelFailure = "an-earlier-vote-ended-the-run-with-a-model-failure";
}
