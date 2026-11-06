using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

/// <summary>Drafts the bill text summary of fixed bills through the real step, and keeps nothing. It reads evidence and sends model calls only.
/// It clears the draft history of each bill, so a bill with a current record is drafted again as a new bill.
/// It has its own token cap and never waits on the monthly dollar cap. When the run ends for any reason, it hands the report to the caller,
/// then adds its real cost to the month once. A call that a cancel stopped counts at its whole reservation, as an interrupted worker call does.</summary>
public sealed class ActionTextMeasure(IActionDraftStore store, ActionDraftStep step, IAiSpendingStore spending)
{
    public async Task RunAsync(IReadOnlyList<CongressBillListItem> bills, long tokenCap, Func<ActionTextMeasureReport, Task> keepReportAsync,
        CancellationToken cancellationToken)
    {
        if (tokenCap < 1) throw new ArgumentOutOfRangeException(nameof(tokenCap), tokenCap, "The spend cap must be at least 1 token.");
        var results = ImmutableArray.CreateBuilder<ActionTextMeasureBill>();
        ActionTextMeasureAttempt? unfinished = null;
        Exception? failure = null;
        try
        {
            var spent = 0L;
            foreach (var bill in bills)
            {
                var result = results.Any(item => item.EndsRun) ? NotRun(bill, results)
                    : await MeasureAsync(bill, tokenCap, spent, attempt => unfinished = attempt, cancellationToken);
                (spent, unfinished) = (spent + result.ChargedTokens, null);
                results.Add(result);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        await FinishAsync(ReportOf(tokenCap, results), unfinished, keepReportAsync, failure);
    }

    private async Task FinishAsync(ActionTextMeasureReport report, ActionTextMeasureAttempt? unfinished,
        Func<ActionTextMeasureReport, Task> keepReportAsync, Exception? failure)
    {
        Exception? chargeFailure = null;
        try
        {
            await keepReportAsync(report);
        }
        finally
        {
            chargeFailure = await TryChargeMonthAsync(report, unfinished);
        }
        if (failure is not null) ExceptionDispatchInfo.Throw(Outcome(failure, chargeFailure));
        if (chargeFailure is not null) ExceptionDispatchInfo.Throw(chargeFailure);
    }

    private static Exception Outcome(Exception failure, Exception? chargeFailure) =>
        failure is OperationCanceledException canceled ? new ActionTextMeasureCanceledException(canceled, chargeFailure)
        : chargeFailure is null ? failure
        : new AggregateException(failure, chargeFailure);

    private async Task<Exception?> TryChargeMonthAsync(ActionTextMeasureReport report, ActionTextMeasureAttempt? unfinished)
    {
        var calls = report.Bills.Select(bill => bill.Usage).Concat(unfinished is null ? [] : [unfinished.Usage]).ToArray();
        var chargedTokens = report.Totals.ChargedTokens + (unfinished is null ? 0 : unfinished.ChargedTokens + unfinished.InFlightTokens);
        try
        {
            await spending.AddChargeAsync(ActionDraftTask.Model, calls, chargedTokens, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

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

    private async Task<ActionTextMeasureBill> MeasureAsync(CongressBillListItem bill, long tokenCap, long spent,
        Action<ActionTextMeasureAttempt> started, CancellationToken cancellationToken)
    {
        var billId = IdOf(bill);
        if (await store.NewestCandidateIdAsync(bill, cancellationToken) is not { } candidateId)
            return ActionTextMeasureBill.Skipped(billId, ActionTextMeasureReason.NoCandidate);
        var evidence = await store.ReadEvidenceAsync(candidateId, withBillText: true, cancellationToken);
        if (evidence?.BillText is not { } billText) return ActionTextMeasureBill.Skipped(billId, ActionTextMeasureReason.NoBillText);

        var attempt = new ActionTextMeasureAttempt(candidateId, tokenCap, spent);
        started(attempt);
        await step.DraftAsync(attempt, evidence with { Current = null, HasStaleDraft = false, IsRejected = false },
            cancellationToken);
        return Summarize(billId, evidence, billText, attempt);
    }

    private static ActionTextMeasureBill Summarize(string billId, ActionDraftEvidence evidence, ActionDraftBillText billText,
        ActionTextMeasureAttempt attempt)
    {
        var rejections = attempt.Runs.SelectMany(run => run.ValidationErrors).ToImmutableArray();
        ActionTextMeasureBill With(ActionTextMeasureOutcome outcome, string? reason, ImmutableArray<ActionTextMeasureSentence> sentences = default) =>
            new(billId, TitleOf(evidence), outcome, reason, billText.CharsRead, billText.CharsTotal, attempt.Calls, attempt.Usage,
                attempt.ChargedTokens, sentences.IsDefault ? [] : sentences, rejections,
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
