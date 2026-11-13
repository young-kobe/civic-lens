using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

/// <summary>The run loop that every text measure shares: one item at a time, the spend charged to one cap, and the later items not run once an item
/// stops the run. The bill measure and the executive summary measure supply how one item is measured and how the report is made.</summary>
internal static class ActionTextMeasureRun
{
    public static async Task<ActionTextMeasureReport> RunAsync<TItem>(IReadOnlyList<TItem> items, long tokenCap, Func<TItem, string> idOf,
        Func<TItem, long, CancellationToken, Task<ActionTextMeasureBill>> measureAsync,
        Func<ImmutableArray<ActionTextMeasureBill>, ActionTextMeasureReport> reportOf, CancellationToken cancellationToken)
    {
        if (tokenCap < 1) throw new ArgumentOutOfRangeException(nameof(tokenCap), tokenCap, "The spend cap must be at least 1 token.");
        var results = ImmutableArray.CreateBuilder<ActionTextMeasureBill>();
        var spent = 0L;
        try
        {
            foreach (var item in items)
            {
                var result = results.Any(earlier => earlier.EndsRun) ? NotRun(idOf(item), results) : await measureAsync(item, spent, cancellationToken);
                spent += result.ChargedTokens;
                results.Add(result);
            }
        }
        catch (OperationCanceledException exception)
        {
            throw new ActionTextMeasureCanceledException(reportOf(results.ToImmutable()), exception);
        }
        return reportOf(results.ToImmutable());
    }

    public static AnalysisTokenUsage UsageOf(IEnumerable<AnalysisRun> runs) =>
        runs.Aggregate(AnalysisTokenUsage.None, (sum, run) => new(sum.InputTokens + run.Usage.InputTokens,
            sum.OutputTokens + run.Usage.OutputTokens, sum.CacheReadTokens + run.Usage.CacheReadTokens, sum.CacheWriteTokens + run.Usage.CacheWriteTokens));

    private static ActionTextMeasureBill NotRun(string id, IEnumerable<ActionTextMeasureBill> earlier) =>
        earlier.Any(item => item.Outcome == ActionTextMeasureOutcome.StoppedByModel)
            ? ActionTextMeasureBill.WithoutCall(id, ActionTextMeasureOutcome.StoppedByModel, ActionTextMeasureReason.ModelFailure)
            : ActionTextMeasureBill.WithoutCall(id, ActionTextMeasureOutcome.StoppedByCap, ActionTextMeasureReason.SpendCap);
}
