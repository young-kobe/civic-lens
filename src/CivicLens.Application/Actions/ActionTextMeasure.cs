using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

/// <summary>Drafts the bill text summary of fixed bills through the real step, and keeps nothing. It reads evidence and sends model calls only.
/// It clears the draft history of each bill, so a bill with a current record is drafted again as a new bill.
/// It has its own token cap and never waits on the monthly dollar cap, but it adds its real cost to the month.</summary>
public sealed class ActionTextMeasure(IActionDraftStore store, ActionDraftStep step, IAiSpendingStore spending)
{
    public async Task<ActionTextMeasureReport> RunAsync(IReadOnlyList<CongressBillListItem> bills, long tokenCap, CancellationToken cancellationToken)
    {
        if (tokenCap < 1) throw new ArgumentOutOfRangeException(nameof(tokenCap), tokenCap, "The spend cap must be at least 1 token.");
        var price = await spending.GetPriceAsync(ActionDraftTask.Model, cancellationToken) ?? throw new InvalidOperationException(
            $"The model {ActionDraftTask.Model} has no price. Save its price on /Review/Spending, then run the measure again.");
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
            var canceled = ReportOf(tokenCap, results);
            await ChargeMonthAsync(price, canceled);
            throw new ActionTextMeasureCanceledException(canceled, exception);
        }
        var report = ReportOf(tokenCap, results);
        await ChargeMonthAsync(price, report);
        return report;
    }

    private Task ChargeMonthAsync(ModelPrice price, ActionTextMeasureReport report) =>
        spending.AddChargeAsync(price.CostMicros(report.Totals.Usage, report.Totals.ChargedTokens), CancellationToken.None);

    private static ActionTextMeasureReport ReportOf(long tokenCap, ImmutableArray<ActionTextMeasureBill>.Builder results) =>
        new(ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion, ActionDraftTask.Model, tokenCap, results.ToImmutable());

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
        var evidence = await store.ReadEvidenceAsync(candidateId, withBillText: true, cancellationToken);
        if (evidence?.BillText is not { } billText) return ActionTextMeasureBill.Skipped(billId, ActionTextMeasureReason.NoBillText);

        var attempt = new ActionTextMeasureAttempt(candidateId, tokenCap, spent);
        await step.DraftAsync(attempt, evidence with { Current = null, HasOlderPromptDraft = false, IsRejected = false },
            cancellationToken);
        return Summarize(billId, evidence, billText, attempt);
    }

    private static ActionTextMeasureBill Summarize(string billId, ActionDraftEvidence evidence, ActionDraftBillText billText,
        ActionTextMeasureAttempt attempt)
    {
        var usage = attempt.Runs.Aggregate(AnalysisTokenUsage.None, (sum, run) => new(sum.InputTokens + run.Usage.InputTokens,
            sum.OutputTokens + run.Usage.OutputTokens, sum.CacheReadTokens + run.Usage.CacheReadTokens, sum.CacheWriteTokens + run.Usage.CacheWriteTokens));
        var rejections = attempt.Runs.SelectMany(run => run.ValidationErrors).ToImmutableArray();
        ActionTextMeasureBill With(ActionTextMeasureOutcome outcome, string? reason, ImmutableArray<ActionTextMeasureSentence> sentences = default) =>
            new(billId, TitleOf(evidence), outcome, reason, billText.CharsRead, billText.CharsTotal, attempt.Calls, usage,
                attempt.Runs.Sum(run => run.ChargedTokens), sentences.IsDefault ? [] : sentences, rejections,
                [.. attempt.Runs.Where(run => run.OutputJson is not null).Select(run => new ActionTextMeasurePass(run.Outcome, run.OutputJson))]);

        if (attempt.RefusedOtherPrompt) return With(ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.NotTextPrompt);
        if (attempt.Failure is { } failure)
            return With(AnalysisFailureRules.NeedsPerson(failure) ? ActionTextMeasureOutcome.StoppedByModel : ActionTextMeasureOutcome.Failed,
                attempt.ErrorCode);
        if (attempt.StoppedByCap) return With(ActionTextMeasureOutcome.StoppedByCap, ActionTextMeasureReason.SpendCap);
        if (attempt.Revision is { TextPackageId: not null } revision)
            return With(ActionTextMeasureOutcome.Accepted, null,
                [.. revision.TextCitations.Select(citation => new ActionTextMeasureSentence(citation.Part, citation.Sentence,
                    billText.Text.Substring(citation.Span.Start, citation.Span.Length)))]);
        if (attempt.Revision is not null) return With(ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.WrittenByCode);
        if (attempt.Calls == 0) return With(ActionTextMeasureOutcome.Skipped, attempt.ErrorCode);
        return With(IsResolverRefusal(attempt.Runs[^1].Outcome) ? ActionTextMeasureOutcome.Rejected : ActionTextMeasureOutcome.Failed, attempt.ErrorCode);
    }

    private static bool IsResolverRefusal(AnalysisRunOutcome outcome) =>
        outcome is AnalysisRunOutcome.Drafted or AnalysisRunOutcome.OutputRejected or AnalysisRunOutcome.CitationRejected;

    private static string? TitleOf(ActionDraftEvidence evidence) =>
        evidence.Pages.Single(page => page.Resource == ApiResource.Bill).Text.Split('\n').FirstOrDefault(text => text.StartsWith(ActionCitations.TitlePrefix, StringComparison.Ordinal))?[ActionCitations.TitlePrefix.Length..];
}
