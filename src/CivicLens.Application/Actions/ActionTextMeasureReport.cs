using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

public enum ActionTextMeasureOutcome { Accepted, Rejected, Failed, Skipped, StoppedByCap, StoppedByModel }

public sealed record ActionTextMeasureSentence(ActionTextPart Part, string Sentence, string Quote, double Grade);

public sealed record ActionTextMeasurePass(AnalysisRunOutcome Outcome, string? OutputJson);

/// <summary>One accepted key term. The definition is the plain form of the quote: the stored characters with each GPO quote mark pair made one plain double quote.
/// Cut is true when the quote ends before the end of its definition paragraph.</summary>
public sealed record ActionTextMeasureKeyTerm(string Term, string Definition, bool Cut);

/// <summary>One key term of the accepted answer that code left out, as the model wrote it.</summary>
public sealed record ActionTextMeasureDroppedKeyTerm(string Term, string Quote);

/// <summary><see cref="DefinitionsHeading"/> is the heading of the definitions section that code found, or null when the bill has none or key terms were off.
/// <see cref="KeyTerms"/> holds the accepted terms, and is empty with no section. With key terms on, a bill with no section is sent the text prompt without key terms. <see cref="MissingDates"/> lists the dates of the sent text that no accepted sentence writes the same way (string match, case ignored). It is filled for a flagged bill only. It is a measure only.</summary>
public sealed record ActionTextMeasureBill(string BillId, string? Title, ActionTextMeasureOutcome Outcome, string? Reason, int CharsRead, int CharsTotal,
    int Calls, AnalysisTokenUsage Usage, long ChargedTokens, ImmutableArray<ActionTextMeasureSentence> Sentences, ImmutableArray<string> Rejections,
    ImmutableArray<ActionTextMeasurePass> Passes, BillFigureCounts TextFigures, BillFigureCounts SentenceFigures, double? PassageGrade,
    ImmutableArray<string> MissingDates, ImmutableArray<ActionTextMeasureKeyTerm> KeyTerms, string? DefinitionsHeading)
{
    /// <summary>The key terms of the accepted answer that code left out. It is empty when code kept every term or no answer was accepted.</summary>
    public ImmutableArray<ActionTextMeasureDroppedKeyTerm> DroppedKeyTerms { get; init; } = [];

    /// <summary>True when the sent text has a dollar amount or a date and the accepted sentences have none of that kind. It is a measure only.
    /// A bill with no accepted sentence is not flagged.</summary>
    public bool LosesFigures => Outcome == ActionTextMeasureOutcome.Accepted &&
        (TextFigures.Amounts > 0 && SentenceFigures.Amounts == 0 || TextFigures.Dates > 0 && SentenceFigures.Dates == 0);

    public bool EndsRun => Outcome is ActionTextMeasureOutcome.StoppedByCap or ActionTextMeasureOutcome.StoppedByModel;

    public static ActionTextMeasureBill Skipped(string billId, string reason) => WithoutCall(billId, ActionTextMeasureOutcome.Skipped, reason);

    public static ActionTextMeasureBill WithoutCall(string billId, ActionTextMeasureOutcome outcome, string reason) =>
        new(billId, null, outcome, reason, 0, 0, 0, AnalysisTokenUsage.None, 0, [], [], [], new(0, 0), new(0, 0), null, [], [], null);
}

public sealed record ActionTextMeasureTotals(int Accepted, int Rejected, int Failed, int Skipped, int StoppedByCap, int StoppedByModel, int Calls,
    AnalysisTokenUsage Usage, long ChargedTokens, double? MeanGrade, int BillsLosingFigures, int BillsWithDefinitions, int BillsWithKeyTerms, int KeyTerms);

public sealed record ActionTextMeasureReport(string PromptVersion, string SchemaVersion, string Model, long TokenCap, bool KeyTermsOn,
    ImmutableArray<ActionTextMeasureBill> Bills)
{
    public ActionTextMeasureTotals Totals => new(Count(ActionTextMeasureOutcome.Accepted), Count(ActionTextMeasureOutcome.Rejected),
        Count(ActionTextMeasureOutcome.Failed), Count(ActionTextMeasureOutcome.Skipped), Count(ActionTextMeasureOutcome.StoppedByCap),
        Count(ActionTextMeasureOutcome.StoppedByModel), Bills.Sum(bill => bill.Calls), TotalUsage(), Bills.Sum(bill => bill.ChargedTokens),
        ActionTextMeasureGrades.Mean(Bills.Select(bill => bill.PassageGrade)), Bills.Count(bill => bill.LosesFigures),
        Bills.Count(bill => bill.DefinitionsHeading is not null), Bills.Count(bill => !bill.KeyTerms.IsEmpty), Bills.Sum(bill => bill.KeyTerms.Length));

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

/// <summary>The mean passage grade of the accepted bills, rounded to one decimal. It is null when no bill was accepted.</summary>
internal static class ActionTextMeasureGrades
{
    public static double? Mean(IEnumerable<double?> passageGrades) =>
        passageGrades.OfType<double>().ToArray() is { Length: > 0 } grades ? Math.Round(grades.Average(), 1) : null;
}
