using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>Summarizes the body text of fixed executive actions through the real step, and keeps nothing. It extends the text measure of bills:
/// the same run loop, attempt, report, and command. It clears the summary of each record, so a record with a summary is summarized again.
/// Each document reports its cut flag and the answers that held a phrase of the closed list.</summary>
public sealed class ExecutiveSummaryMeasure(IExecutiveSummaryStore store, ExecutiveSummaryStep step)
{
    private static readonly IReadOnlySet<string> MeasuredPrompts = new HashSet<string> { ExecutiveSummaryTask.PromptVersion };

    public Task<ActionTextMeasureReport> RunAsync(IReadOnlyList<string> documentNumbers, long tokenCap, CancellationToken cancellationToken) =>
        ActionTextMeasureRun.RunAsync(documentNumbers, tokenCap, number => number, (number, spent, token) => MeasureAsync(number, tokenCap, spent, token),
            results => new(ExecutiveSummaryTask.PromptVersion, ExecutiveSummaryTask.SchemaVersion, ExecutiveSummaryTask.Model, tokenCap, false, results),
            cancellationToken);

    private async Task<ActionTextMeasureBill> MeasureAsync(string number, long tokenCap, long spent, CancellationToken cancellationToken)
    {
        if (await store.FindRecordIdAsync(number, cancellationToken) is not { } recordId)
            return ActionTextMeasureBill.Skipped(number, ActionTextMeasureReason.NoRecord);
        if (await store.ReadEvidenceAsync(recordId, cancellationToken) is not { } evidence)
            return ActionTextMeasureBill.Skipped(number, ActionTextMeasureReason.NoDocumentText);
        var attempt = new AnalysisMeasureAttempt<ExecutiveSummaryRun, ExecutiveActionRevision>(recordId, ExecutiveSummaryTask.Identity, MeasuredPrompts,
            run => run.Run, checkpoint => (checkpoint as ExecutiveSummaryCheckpoint)?.Revision, tokenCap, spent);
        await step.SummarizeAsync(attempt, evidence with { Current = evidence.Current with { Summary = null }, IsRejected = false }, cancellationToken);
        return Summarize(number, evidence, attempt);
    }

    private static ActionTextMeasureBill Summarize(string number, ExecutiveSummaryEvidence evidence,
        AnalysisMeasureAttempt<ExecutiveSummaryRun, ExecutiveActionRevision> attempt)
    {
        var text = evidence.Text;
        var rejections = attempt.Runs.SelectMany(run => run.ValidationErrors).ToImmutableArray();
        ActionTextMeasureBill With(ActionTextMeasureOutcome outcome, string? reason, ImmutableArray<ActionTextMeasureSentence> sentences = default,
            double? passageGrade = null)
        {
            var kept = sentences.IsDefault ? [] : sentences;
            var sentenceText = string.Join('\n', kept.Select(sentence => sentence.Sentence));
            var bill = new ActionTextMeasureBill(number, evidence.Current.Title, outcome, reason, text.CharsRead, text.CharsTotal, attempt.Calls,
                ActionTextMeasureRun.UsageOf(attempt.Runs), attempt.Runs.Sum(run => run.ChargedTokens), kept, rejections,
                [.. attempt.Runs.Where(run => run.OutputJson is not null).Select(run => new ActionTextMeasurePass(run.Outcome, run.OutputJson))],
                BillFigures.Count(text.Body), BillFigures.Count(sentenceText), passageGrade, [], [], null)
            {
                Cut = text.Cut,
                PhraseRejections = attempt.Runs.Count(run => run.ValidationErrors.Any(AiTextPhrases.IsReason))
            };
            return bill.LosesFigures ? bill with { MissingDates = [.. BillFigures.MissingDates(text.Body, sentenceText)] } : bill;
        }

        if (attempt.RefusedOtherPrompt) return With(ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.NotTextPrompt);
        if (attempt.Failure is { } failure)
            return With(AnalysisFailureRules.NeedsPerson(failure) ? ActionTextMeasureOutcome.StoppedByModel : ActionTextMeasureOutcome.Failed, attempt.ErrorCode);
        if (attempt.StoppedByCap) return With(ActionTextMeasureOutcome.StoppedByCap, ActionTextMeasureReason.SpendCap);
        if (attempt.Revision is { Summary: { } summary })
            return With(ActionTextMeasureOutcome.Accepted, null,
                [.. summary.Sentences.Select(sentence => new ActionTextMeasureSentence(ActionTextPart.Summary, sentence.Sentence,
                    text.Text.Substring(sentence.Quote.Start, sentence.Quote.Length), ReadingGrade.Rounded(sentence.Sentence)))],
                ReadingGrade.RoundedPassage([.. summary.Sentences.Select(sentence => sentence.Sentence)]));
        if (attempt.Calls == 0) return With(ActionTextMeasureOutcome.Skipped, attempt.ErrorCode);
        return With(attempt.Runs[^1].Outcome is AnalysisRunOutcome.OutputRejected or AnalysisRunOutcome.CitationRejected
            ? ActionTextMeasureOutcome.Rejected : ActionTextMeasureOutcome.Failed, attempt.ErrorCode);
    }
}
