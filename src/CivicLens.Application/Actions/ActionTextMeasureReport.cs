using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

public enum ActionTextMeasureOutcome { Accepted, Rejected, Skipped, StoppedByCap }

public sealed record ActionTextMeasureSentence(ActionTextPart Part, string Sentence, string Quote);

public sealed record ActionTextMeasureBill(string BillId, ActionTextMeasureOutcome Outcome, string? Reason, int CharsRead, int CharsTotal,
    int Calls, AnalysisTokenUsage Usage, ImmutableArray<ActionTextMeasureSentence> Sentences, ImmutableArray<string> Rejections)
{
    public static ActionTextMeasureBill Skipped(string billId, string reason) =>
        new(billId, ActionTextMeasureOutcome.Skipped, reason, 0, 0, 0, AnalysisTokenUsage.None, [], []);

    public static ActionTextMeasureBill NotRun(string billId) =>
        new(billId, ActionTextMeasureOutcome.StoppedByCap, ActionTextMeasureReason.SpendCap, 0, 0, 0, AnalysisTokenUsage.None, [], []);
}

public sealed record ActionTextMeasureTotals(int Accepted, int Rejected, int Skipped, int StoppedByCap, int Calls, AnalysisTokenUsage Usage);

public sealed record ActionTextMeasureReport(string PromptVersion, string SchemaVersion, string Model, long TokenCap,
    ImmutableArray<ActionTextMeasureBill> Bills)
{
    public ActionTextMeasureTotals Totals => new(Count(ActionTextMeasureOutcome.Accepted), Count(ActionTextMeasureOutcome.Rejected),
        Count(ActionTextMeasureOutcome.Skipped), Count(ActionTextMeasureOutcome.StoppedByCap), Bills.Sum(bill => bill.Calls), TotalUsage());

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
    public const string WrittenByCode = "step-wrote-the-text-by-code";
}
