using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

public enum ActionMeasuredPrompt { BillText, BillLine }

/// <summary>Drafts one prompt of the step on fixed bills through the real step, and keeps nothing. <see cref="ActionMeasuredPrompt.BillText"/> measures the bill
/// text summary. <see cref="ActionMeasuredPrompt.BillLine"/> measures the summary prompt that also writes the bill line, on bills that have a CRS summary.
/// It reads evidence and sends model calls only. It clears the draft history of each bill, so a bill with a current record is drafted again as a new bill.</summary>
public sealed class ActionTextMeasure(IActionDraftStore store, ActionDraftStep step, ActionMeasuredPrompt prompt = ActionMeasuredPrompt.BillText)
{
    private bool MeasuresBillText => prompt == ActionMeasuredPrompt.BillText;

    private string MeasuredPromptVersion => MeasuresBillText ? ActionDraftTask.TextPromptVersion : ActionDraftTask.PromptVersion;

    public async Task<ActionTextMeasureReport> RunAsync(IReadOnlyList<CongressBillListItem> bills, long tokenCap, CancellationToken cancellationToken)
    {
        if (tokenCap < 1) throw new ArgumentOutOfRangeException(nameof(tokenCap), tokenCap, "The spend cap must be at least 1 token.");
        var results = ImmutableArray.CreateBuilder<ActionTextMeasureBill>();
        var spent = 0L;
        try
        {
            foreach (var bill in bills)
            {
                var result = results.Any(item => item.EndsRun) ? NotRun(bill, results) : await MeasureAsync(bill, tokenCap, spent, cancellationToken);
                spent += result.ChargedTokens;
                results.Add(result);
            }
        }
        catch (OperationCanceledException exception)
        {
            throw new ActionTextMeasureCanceledException(ReportOf(tokenCap, results), exception);
        }
        return ReportOf(tokenCap, results);
    }

    private ActionTextMeasureReport ReportOf(long tokenCap, ImmutableArray<ActionTextMeasureBill>.Builder results) =>
        new(MeasuredPromptVersion, MeasuresBillText ? ActionDraftTask.TextSchemaVersion : ActionDraftTask.SchemaVersion, ActionDraftTask.Model, tokenCap,
            results.ToImmutable());

    private static ActionTextMeasureBill NotRun(CongressBillListItem bill, IEnumerable<ActionTextMeasureBill> earlier) =>
        earlier.Any(item => item.Outcome == ActionTextMeasureOutcome.StoppedByModel)
            ? ActionTextMeasureBill.WithoutCall(IdOf(bill), ActionTextMeasureOutcome.StoppedByModel, ActionTextMeasureReason.ModelFailure)
            : ActionTextMeasureBill.WithoutCall(IdOf(bill), ActionTextMeasureOutcome.StoppedByCap, ActionTextMeasureReason.SpendCap);

    public static string IdOf(CongressBillListItem bill) => $"{bill.Congress}-{bill.Type}-{bill.Number}";

    /// <summary>Reads an ID such as 119-hr-1943. Only the exact form that <see cref="IdOf"/> writes is valid.</summary>
    public static CongressBillListItem? TryParseId(string id) =>
        id.Split('-') is [var congress, var type, var number] && CongressBillListItem.TryReadIdentity(congress, type, number) is { } bill &&
        IdOf(bill) == id ? bill : null;

    private async Task<ActionTextMeasureBill> MeasureAsync(CongressBillListItem bill, long tokenCap, long spent, CancellationToken cancellationToken)
    {
        var billId = IdOf(bill);
        if (await store.NewestCandidateIdAsync(bill, cancellationToken) is not { } candidateId)
            return ActionTextMeasureBill.Skipped(billId, ActionTextMeasureReason.NoCandidate);
        var evidence = await store.ReadEvidenceAsync(candidateId, withBillText: MeasuresBillText, cancellationToken);
        if (evidence is null) return ActionTextMeasureBill.Skipped(billId, MeasuresBillText ? ActionTextMeasureReason.NoBillText : ActionDraftErrorCodes.EvidenceUnavailable);
        if (MeasuresBillText && evidence.BillText is null) return ActionTextMeasureBill.Skipped(billId, ActionTextMeasureReason.NoBillText);

        var attempt = new ActionTextMeasureAttempt(candidateId, tokenCap, spent, MeasuredPromptVersion);
        await step.DraftAsync(attempt, evidence with { Current = null, HasStaleDraft = false, IsRejected = false },
            cancellationToken);
        return MeasuresBillText ? Summarize(billId, evidence, evidence.BillText!, attempt) : SummarizeLine(billId, evidence, attempt);
    }

    private static ActionTextMeasureBill SummarizeLine(string billId, ActionDraftEvidence evidence, ActionTextMeasureAttempt attempt)
    {
        var bill = ActionTextMeasureBill.WithoutCall(billId, ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.NotSummaryPrompt) with
        {
            Title = TitleOf(evidence),
            Calls = attempt.Calls,
            Usage = UsageOf(attempt),
            ChargedTokens = attempt.Runs.Sum(run => run.ChargedTokens),
            Rejections = [.. attempt.Runs.SelectMany(run => run.ValidationErrors)],
            Passes = PassesOf(attempt)
        };
        if (attempt.RefusedOtherPrompt) return bill;
        if (attempt.Failure is { } failure)
            return bill with { Outcome = AnalysisFailureRules.NeedsPerson(failure) ? ActionTextMeasureOutcome.StoppedByModel : ActionTextMeasureOutcome.Failed, Reason = attempt.ErrorCode };
        if (attempt.StoppedByCap) return bill with { Outcome = ActionTextMeasureOutcome.StoppedByCap, Reason = ActionTextMeasureReason.SpendCap };
        if (attempt.Revision is not null && attempt.Calls > 0)
        {
            var line = LineOf(attempt);
            return bill with { Outcome = ActionTextMeasureOutcome.Accepted, Reason = null, BillLine = line, PassageGrade = line.Grade };
        }
        if (attempt.Revision is not null) return bill with { Reason = ActionTextMeasureReason.WrittenByCode };
        if (attempt.Calls == 0) return bill with { Reason = attempt.ErrorCode };
        return bill with { Outcome = IsResolverRefusal(attempt.Runs[^1].Outcome) ? ActionTextMeasureOutcome.Rejected : ActionTextMeasureOutcome.Failed, Reason = attempt.ErrorCode };
    }

    /// <summary>Reads the line that the model wrote in its last answer. The revision holds the line only when the checks kept it.</summary>
    private static ActionTextMeasureLine LineOf(ActionTextMeasureAttempt attempt)
    {
        var written = HeadlineSummaryOutput.Parse(attempt.Runs[^1].OutputJson ?? string.Empty)?.BillLine;
        var line = written?.Line?.Trim();
        return new(line, written?.Quote, attempt.Revision!.BillLine is not null, line?.Length ?? 0, string.IsNullOrEmpty(line) ? null : ReadingGrade.Rounded(line));
    }

    private static AnalysisTokenUsage UsageOf(ActionTextMeasureAttempt attempt) => attempt.Runs.Aggregate(AnalysisTokenUsage.None, (sum, run) => new(
        sum.InputTokens + run.Usage.InputTokens, sum.OutputTokens + run.Usage.OutputTokens, sum.CacheReadTokens + run.Usage.CacheReadTokens,
        sum.CacheWriteTokens + run.Usage.CacheWriteTokens));

    private static ImmutableArray<ActionTextMeasurePass> PassesOf(ActionTextMeasureAttempt attempt) =>
        [.. attempt.Runs.Where(run => run.OutputJson is not null).Select(run => new ActionTextMeasurePass(run.Outcome, run.OutputJson))];

    private static ActionTextMeasureBill Summarize(string billId, ActionDraftEvidence evidence, ActionDraftBillText billText,
        ActionTextMeasureAttempt attempt)
    {
        var usage = UsageOf(attempt);
        var rejections = attempt.Runs.SelectMany(run => run.ValidationErrors).ToImmutableArray();
        var textFigures = BillFigures.Count(billText.Body);
        ActionTextMeasureBill With(ActionTextMeasureOutcome outcome, string? reason, ImmutableArray<ActionTextMeasureSentence> sentences = default, double? passageGrade = null)
        {
            var kept = sentences.IsDefault ? [] : sentences;
            var sentenceText = string.Join('\n', kept.Select(sentence => sentence.Sentence));
            var bill = new ActionTextMeasureBill(billId, TitleOf(evidence), outcome, reason, billText.CharsRead, billText.CharsTotal, attempt.Calls, usage,
                attempt.Runs.Sum(run => run.ChargedTokens), kept, rejections,
                PassesOf(attempt),
                textFigures, BillFigures.Count(sentenceText), passageGrade, []);
            return bill.LosesFigures ? bill with { MissingDates = [.. BillFigures.MissingDates(billText.Body, sentenceText)] } : bill;
        }

        if (attempt.RefusedOtherPrompt) return With(ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.NotTextPrompt);
        if (attempt.Failure is { } failure)
            return With(AnalysisFailureRules.NeedsPerson(failure) ? ActionTextMeasureOutcome.StoppedByModel : ActionTextMeasureOutcome.Failed,
                attempt.ErrorCode);
        if (attempt.StoppedByCap) return With(ActionTextMeasureOutcome.StoppedByCap, ActionTextMeasureReason.SpendCap);
        if (attempt.Revision is { TextPackageId: not null } revision)
            return With(ActionTextMeasureOutcome.Accepted, null,
                [.. revision.TextCitations.Select(citation => new ActionTextMeasureSentence(citation.Part, citation.Sentence,
                    billText.Text.Substring(citation.Span.Start, citation.Span.Length), ReadingGrade.Rounded(citation.Sentence)))],
                ReadingGrade.RoundedPassage([revision.Headline, .. revision.TextCitations.Select(citation => citation.Sentence)]));
        if (attempt.Revision is not null) return With(ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.WrittenByCode);
        if (attempt.Calls == 0) return With(ActionTextMeasureOutcome.Skipped, attempt.ErrorCode);
        return With(IsResolverRefusal(attempt.Runs[^1].Outcome) ? ActionTextMeasureOutcome.Rejected : ActionTextMeasureOutcome.Failed, attempt.ErrorCode);
    }

    private static bool IsResolverRefusal(AnalysisRunOutcome outcome) =>
        outcome is AnalysisRunOutcome.Drafted or AnalysisRunOutcome.OutputRejected or AnalysisRunOutcome.CitationRejected;

    private static string? TitleOf(ActionDraftEvidence evidence) =>
        evidence.Pages.Single(page => page.Resource == ApiResource.Bill).Text.Split('\n').FirstOrDefault(text => text.StartsWith(ActionCitations.TitlePrefix, StringComparison.Ordinal))?[ActionCitations.TitlePrefix.Length..];
}
