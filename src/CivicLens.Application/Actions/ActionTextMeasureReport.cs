using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

public enum ActionTextMeasureOutcome { Accepted, Rejected, Failed, Skipped, StoppedByCap, StoppedByModel }

public sealed record ActionTextMeasureSentence(ActionTextPart Part, string Sentence, string Quote, double Grade);

public sealed record ActionTextMeasurePass(AnalysisRunOutcome Outcome, string? OutputJson);

public sealed record ActionTextMeasureBill(string BillId, string? Title, ActionTextMeasureOutcome Outcome, string? Reason, int CharsRead, int CharsTotal,
    int Calls, AnalysisTokenUsage Usage, long ChargedTokens, ImmutableArray<ActionTextMeasureSentence> Sentences, ImmutableArray<string> Rejections,
    ImmutableArray<ActionTextMeasurePass> Passes)
{
    public double? MeanGrade => ActionTextMeasureGrades.Mean(Sentences);

    public bool EndsRun => Outcome is ActionTextMeasureOutcome.StoppedByCap or ActionTextMeasureOutcome.StoppedByModel;

    public static ActionTextMeasureBill Skipped(string billId, string reason) => WithoutCall(billId, ActionTextMeasureOutcome.Skipped, reason);

    public static ActionTextMeasureBill WithoutCall(string billId, ActionTextMeasureOutcome outcome, string reason) =>
        new(billId, null, outcome, reason, 0, 0, 0, AnalysisTokenUsage.None, 0, [], [], []);
}

public sealed record ActionTextMeasureTotals(int Accepted, int Rejected, int Failed, int Skipped, int StoppedByCap, int StoppedByModel, int Calls,
    AnalysisTokenUsage Usage, long ChargedTokens, double? MeanGrade);

public sealed record ActionTextMeasureReport(string PromptVersion, string SchemaVersion, string Model, long TokenCap,
    ImmutableArray<ActionTextMeasureBill> Bills)
{
    public ActionTextMeasureTotals Totals => new(Count(ActionTextMeasureOutcome.Accepted), Count(ActionTextMeasureOutcome.Rejected),
        Count(ActionTextMeasureOutcome.Failed), Count(ActionTextMeasureOutcome.Skipped), Count(ActionTextMeasureOutcome.StoppedByCap),
        Count(ActionTextMeasureOutcome.StoppedByModel), Bills.Sum(bill => bill.Calls), TotalUsage(), Bills.Sum(bill => bill.ChargedTokens),
        ActionTextMeasureGrades.Mean(Bills.SelectMany(bill => bill.Sentences)));

    private int Count(ActionTextMeasureOutcome outcome) => Bills.Count(bill => bill.Outcome == outcome);

    private AnalysisTokenUsage TotalUsage() => Bills.Aggregate(AnalysisTokenUsage.None, (sum, bill) => new(sum.InputTokens + bill.Usage.InputTokens,
        sum.OutputTokens + bill.Usage.OutputTokens, sum.CacheReadTokens + bill.Usage.CacheReadTokens, sum.CacheWriteTokens + bill.Usage.CacheWriteTokens));
}

public static class ActionTextMeasureReason
{
    public const string NoCandidate = "no-candidate";
    public const string NoBillText = "no-bill-text";
    public const string NotTextPrompt = "step-does-not-use-the-text-prompt";
    public const string SpendCap = "spend-cap";
    public const string ModelFailure = "an-earlier-bill-ended-the-run-with-a-model-failure";
    public const string WrittenByCode = "step-wrote-the-text-by-code";
}

/// <summary>The mean grade of the accepted sentences, rounded to one decimal. It is null when no sentence was accepted.</summary>
internal static class ActionTextMeasureGrades
{
    public static double? Mean(IEnumerable<ActionTextMeasureSentence> sentences) =>
        sentences.Select(sentence => sentence.Grade).ToArray() is { Length: > 0 } grades ? Math.Round(grades.Average(), 1) : null;
}
