using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

/// <summary>Drafts the bill text summary of fixed bills through the real step, and keeps nothing. It reads evidence and sends model calls only.
/// It clears the draft history of each bill, so a bill with a current record is drafted again as a new bill.</summary>
public sealed class ActionTextMeasure(IActionDraftStore store, ActionDraftStep step)
{
    public async Task<ActionTextMeasureReport> RunAsync(IReadOnlyList<CongressBillListItem> bills, long tokenCap, CancellationToken cancellationToken)
    {
        if (tokenCap < 1) throw new ArgumentOutOfRangeException(nameof(tokenCap), tokenCap, "The spend cap must be at least 1 token.");
        var results = ImmutableArray.CreateBuilder<ActionTextMeasureBill>();
        var spent = 0L;
        foreach (var bill in bills)
        {
            var result = results.Any(item => item.Outcome == ActionTextMeasureOutcome.StoppedByCap)
                ? ActionTextMeasureBill.NotRun(IdOf(bill))
                : await MeasureAsync(bill, tokenCap, spent, cancellationToken);
            spent += result.Usage.Total;
            results.Add(result);
        }
        return new(ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion, ActionDraftTask.Model, tokenCap, results.ToImmutable());
    }

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
        await step.DraftAsync(attempt, evidence with { Current = null, HasNewerCandidate = false, HasOlderPromptDraft = false, IsRejected = false },
            cancellationToken);
        return Summarize(billId, billText, attempt);
    }

    private static ActionTextMeasureBill Summarize(string billId, ActionDraftBillText billText, ActionTextMeasureAttempt attempt)
    {
        var usage = attempt.Runs.Aggregate(AnalysisTokenUsage.None, (sum, run) => new(sum.InputTokens + run.Usage.InputTokens,
            sum.OutputTokens + run.Usage.OutputTokens, sum.CacheReadTokens + run.Usage.CacheReadTokens, sum.CacheWriteTokens + run.Usage.CacheWriteTokens));
        var rejections = attempt.Runs.SelectMany(run => run.ValidationErrors).ToImmutableArray();
        ActionTextMeasureBill With(ActionTextMeasureOutcome outcome, string? reason, ImmutableArray<ActionTextMeasureSentence> sentences = default) =>
            new(billId, outcome, reason, billText.CharsRead, billText.CharsTotal, attempt.Calls, usage, sentences.IsDefault ? [] : sentences, rejections);

        if (attempt.RefusedOtherPrompt) return With(ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.NotTextPrompt);
        if (attempt.StoppedByCap) return With(ActionTextMeasureOutcome.StoppedByCap, ActionTextMeasureReason.SpendCap);
        if (attempt.Revision is { TextPackageId: not null } revision)
            return With(ActionTextMeasureOutcome.Accepted, null,
                [.. revision.TextCitations.Select(citation => new ActionTextMeasureSentence(citation.Part, citation.Sentence,
                    billText.Text.Substring(citation.Span.Start, citation.Span.Length)))]);
        if (attempt.Revision is not null) return With(ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.WrittenByCode);
        return With(attempt.Calls == 0 ? ActionTextMeasureOutcome.Skipped : ActionTextMeasureOutcome.Rejected, attempt.ErrorCode);
    }
}
