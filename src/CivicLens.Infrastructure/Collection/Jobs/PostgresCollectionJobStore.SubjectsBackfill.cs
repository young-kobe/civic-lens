using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Infrastructure.Collection.BillStream;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IBillSubjectsBackfillStore
{
    public async Task<IReadOnlyList<string>> ListNewestBillsWithoutSubjectCountAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var gates = db.Set<BillGateRow>().AsNoTracking();
        return await gates.Where(gate => gate.SubjectCount == 0 && !gates.Any(newer => newer.SourceId == gate.SourceId &&
                newer.Congress == gate.Congress && newer.BillType == gate.BillType && newer.BillNumber == gate.BillNumber &&
                newer.UpdateDateUtcTicks > gate.UpdateDateUtcTicks))
            .OrderBy(gate => gate.DetailJobId).Select(gate => gate.DetailJobId).Take(limit).ToListAsync(cancellationToken);
    }

    public Task<bool> AdmitSubjectsChainAsync(ConfiguredCollectionSource source, string detailJobId, int subjectCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailJobId);
        return TransactionAsync(async (db, now) =>
        {
            var gate = await db.Set<BillGateRow>().SingleAsync(row => row.DetailJobId == detailJobId, cancellationToken);
            if (gate.SubjectCount != 0 || subjectCount == 0) return false;
            gate.SubjectCount = subjectCount;
            if (!gate.HasSubjectsChain) return false;
            var first = source.CreateFirstPageJob(ApiResource.BillSubjects, IdentityOf(gate), UpdateDateOf(gate), Today(now));
            var result = await AdmitFollowOnsAsync(db, source, detailJobId, [first], now, cancellationToken);
            return result.Jobs.Count > 0;
        }, cancellationToken);
    }
}
