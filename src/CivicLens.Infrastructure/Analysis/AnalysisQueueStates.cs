using System.Linq.Expressions;
using CivicLens.Infrastructure.Analysis.Persistence;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Analysis;

internal static class AnalysisQueueStates
{
    private static readonly State[] PendingStates = [State.Pending, State.Running, State.RetryWaiting, State.WaitingForBudget];
    private static readonly State[] FailedStates = [State.Failed, State.Blocked];

    public static Expression<Func<TRow, bool>> IsPending<TRow>() where TRow : class, IAnalysisRow =>
        row => PendingStates.Contains(row.Status);

    public static Expression<Func<TRow, bool>> IsFailed<TRow>() where TRow : class, IAnalysisRow =>
        row => FailedStates.Contains(row.Status);

    public static bool IsFailed(State status) => FailedStates.Contains(status);
}
