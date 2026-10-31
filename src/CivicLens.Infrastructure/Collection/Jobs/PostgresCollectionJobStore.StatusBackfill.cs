using CivicLens.Application.Collection.Bills;
using CivicLens.Infrastructure.Collection.BillStream;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IBillStatusBackfillStore
{
    public async Task<IReadOnlyList<string>> ListNewestBillsWithoutStatusAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var gates = db.Set<BillGateRow>().AsNoTracking();
        return await gates.Where(gate => !gate.LatestActionRead && !gates.Any(newer => newer.SourceId == gate.SourceId &&
                newer.Congress == gate.Congress && newer.BillType == gate.BillType && newer.BillNumber == gate.BillNumber &&
                newer.UpdateDateUtcTicks > gate.UpdateDateUtcTicks))
            .OrderBy(gate => gate.DetailJobId).Select(gate => gate.DetailJobId).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<bool> RecordStatusAsync(string detailJobId, CongressBillLatestAction? latestAction, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detailJobId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var gate = await db.Set<BillGateRow>().SingleAsync(row => row.DetailJobId == detailJobId, cancellationToken);
        if (gate.LatestActionRead) return false;
        gate.LatestActionRead = true;
        gate.LatestActionDate = latestAction?.Date;
        gate.LatestActionText = latestAction?.Text;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
