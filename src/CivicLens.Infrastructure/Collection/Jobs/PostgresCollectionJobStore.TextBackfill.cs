using CivicLens.Application.Actions;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection.BillStream;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IBillTextBackfillStore
{
    public async Task<int> CountPendingActionDraftsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<ActionAnalysisRow>().AsNoTracking().Where(row => row.Task == ActionDraftTask.Task)
            .Where(AnalysisQueueStates.IsPending<ActionAnalysisRow>()).CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BillTextBackfillTarget>> ListNewestBillsWithoutTextAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ToTargetsAsync(NewestBillsWithoutText(db).Take(limit), cancellationToken);
    }

    public async Task<IReadOnlyList<BillTextBackfillTarget>> ListNewestBillsWithoutTextAsync(IReadOnlyList<CongressBillListItem> bills,
        CancellationToken cancellationToken)
    {
        var ids = bills.Select(ActionTextMeasure.IdOf).ToArray();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ToTargetsAsync(NewestBillsWithoutText(db)
            .Where(gate => ids.Contains(gate.Congress + "-" + gate.BillType + "-" + gate.BillNumber)), cancellationToken);
    }

    public Task<bool> AdmitTextVersionsChainAsync(ConfiguredCollectionSource source, string detailJobId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailJobId);
        return TransactionAsync(async (db, now) =>
        {
            var gate = await db.Set<BillGateRow>().SingleAsync(row => row.DetailJobId == detailJobId, cancellationToken);
            if (gate.HasSummariesChain || gate.TextState is not null) return false;
            var first = source.CreateFirstPageJob(ApiResource.BillTextVersions, IdentityOf(gate), UpdateDateOf(gate), Today(now));
            var result = await AdmitFollowOnsAsync(db, source, detailJobId, [first], now, cancellationToken);
            return result.Jobs.Count > 0;
        }, cancellationToken);
    }

    private static IQueryable<BillGateRow> NewestBillsWithoutText(CollectionAttemptDbContext db)
    {
        var gates = db.Set<BillGateRow>().AsNoTracking();
        return gates.Where(gate => gate.SummaryCount == 0 && gate.TextState == null && !gates.Any(newer =>
                newer.SourceId == gate.SourceId && newer.Congress == gate.Congress && newer.BillType == gate.BillType &&
                newer.BillNumber == gate.BillNumber && newer.UpdateDateUtcTicks > gate.UpdateDateUtcTicks))
            .OrderBy(gate => gate.DetailJobId);
    }

    private static async Task<IReadOnlyList<BillTextBackfillTarget>> ToTargetsAsync(IQueryable<BillGateRow> gates,
        CancellationToken cancellationToken) =>
        await gates.Select(gate => new BillTextBackfillTarget(gate.DetailJobId, gate.SourceId)).ToListAsync(cancellationToken);
}
