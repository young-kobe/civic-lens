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
    public async Task<IReadOnlyList<BillTextBackfillTarget>> ListNewestBillsWithoutTextAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ToTargetsAsync(NewestBillsWithoutText(db).Take(limit), cancellationToken);
    }

    public async Task<BillTextListing> ListNewestBillsWithoutTextAsync(IReadOnlyList<CongressBillListItem> bills,
        CancellationToken cancellationToken)
    {
        var ids = bills.Select(ActionTextMeasure.IdOf).ToArray();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var targets = await ToTargetsAsync(NewestBillsWithoutText(db).Where(gate => ids.Contains(gate.Congress + "-" + gate.BillType + "-" + gate.BillNumber)), cancellationToken);
        var unlisted = ids.Except(targets.Select(target => target.BillId)).ToArray();
        var gated = await db.Set<BillGateRow>().AsNoTracking().Where(gate => unlisted.Contains(gate.Congress + "-" + gate.BillType + "-" + gate.BillNumber))
            .Select(gate => new { BillId = gate.Congress + "-" + gate.BillType + "-" + gate.BillNumber, gate.SummaryCount })
            .ToListAsync(cancellationToken);
        var summarized = gated.Where(gate => gate.SummaryCount > 0).Select(gate => gate.BillId).ToHashSet();
        var gatedIds = gated.Select(gate => gate.BillId).ToHashSet();
        return new(targets, [.. unlisted.Select(id => new SkippedBill(id, SkipReasonOf(id, gatedIds, summarized)))]);
    }

    public Task<bool> AdmitTextVersionsChainAsync(ConfiguredCollectionSource source, string detailJobId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailJobId);
        return TransactionAsync(async (db, now) =>
        {
            var gate = await db.Set<BillGateRow>().SingleAsync(row => row.DetailJobId == detailJobId, cancellationToken);
            if (gate.HasSummariesChain || gate.TextState is not null) return false;
            if (!await NewestOfItsBill(db.Set<BillGateRow>().AsNoTracking()).AnyAsync(row => row.DetailJobId == detailJobId, cancellationToken)) return false;
            var first = source.CreateFirstPageJob(ApiResource.BillTextVersions, IdentityOf(gate), UpdateDateOf(gate), Today(now));
            var result = await AdmitFollowOnsAsync(db, source, detailJobId, [first], now, cancellationToken);
            return result.Jobs.Count > 0;
        }, cancellationToken);
    }

    private static IQueryable<BillGateRow> NewestBillsWithoutText(CollectionAttemptDbContext db) =>
        NewestOfItsBill(db.Set<BillGateRow>().AsNoTracking()).Where(gate => gate.SummaryCount == 0 && gate.TextState == null)
            .OrderBy(gate => gate.DetailJobId);

    private static IQueryable<BillGateRow> NewestOfItsBill(IQueryable<BillGateRow> gates) =>
        gates.Where(gate => !gates.Any(newer =>
            newer.SourceId == gate.SourceId && newer.Congress == gate.Congress && newer.BillType == gate.BillType &&
            newer.BillNumber == gate.BillNumber && newer.UpdateDateUtcTicks > gate.UpdateDateUtcTicks));

    private static BillTextSkipReason SkipReasonOf(string billId, IReadOnlySet<string> gatedIds, IReadOnlySet<string> summarizedIds) =>
        !gatedIds.Contains(billId) ? BillTextSkipReason.NoGate
            : summarizedIds.Contains(billId) ? BillTextSkipReason.HasSummary
            : BillTextSkipReason.HasTextState;

    private static async Task<IReadOnlyList<BillTextBackfillTarget>> ToTargetsAsync(IQueryable<BillGateRow> gates,
        CancellationToken cancellationToken) =>
        await gates.Select(gate => new BillTextBackfillTarget(gate.DetailJobId, gate.SourceId, gate.Congress + "-" + gate.BillType + "-" + gate.BillNumber))
            .ToListAsync(cancellationToken);
}
