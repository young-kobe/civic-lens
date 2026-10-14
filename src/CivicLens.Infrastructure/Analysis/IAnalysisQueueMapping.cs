using System.Linq.Expressions;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;

namespace CivicLens.Infrastructure.Analysis;

internal readonly record struct AnalysisSettlement<TRun>(AnalysisCheckpoint Checkpoint, TRun? Run) where TRun : class;

internal interface IAnalysisQueueMapping<TRow, TRun> where TRow : class, IAnalysisRow where TRun : class
{
    string Table { get; }
    string KeyColumn { get; }
    string KeyProperty { get; }
    string BudgetTable { get; }
    string? BudgetTask { get; }
    AnalysisTaskIdentity Identity { get; }
    bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint);
    AnalysisRun RunOf(TRun run);
    TRun WrapInterrupted(AnalysisRun run);
    void AddRun(CollectionAttemptDbContext db, TRow row, TRun run);

    Expression<Func<TRow, bool>>? ClaimedFirst(CollectionAttemptDbContext db) => null;

    Task<AnalysisSettlement<TRun>> PrepareSettlementAsync(CollectionAttemptDbContext db, TRow row, AnalysisSettlement<TRun> settlement,
        CancellationToken cancellationToken) => Task.FromResult(settlement);

    Task AfterTransitionAsync(CollectionAttemptDbContext db, TRow row, AnalysisCheckpoint checkpoint, long now,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
