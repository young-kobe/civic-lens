using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IBillStreamAdmissionStore
{
    public Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        BillStreamWindow window, CongressBillListPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(listJobId);
        return TransactionAsync(async (db, now) =>
        {
            var admission = source.CreateBillStreamAdmission(listJobId, window, page, DateOnly.FromDateTime(now.UtcDateTime));
            var (result, admitted) = await AdmitApiJobsAsync(db, admission.Batch, ApiJobSaveBatchSize, now, cancellationToken);
            if (result.DeferredCount > 0)
                throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold a full bill list page.");
            var admittedBills = admitted.Count(item => item.Job != admission.NextPage);
            await RecordPassAsync(db, listJobId, admission, admittedBills, page.UnknownTypeCount, now, cancellationToken);
            return result;
        }, cancellationToken);
    }

    private static async Task RecordPassAsync(CollectionAttemptDbContext db, string listJobId,
        BillStreamAdmission admission, int admittedBills, int unknownTypeCount, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await db.Set<BillStreamPageRow>().AnyAsync(row => row.ListJobId == listJobId, cancellationToken)) return;
        db.Add(new BillStreamPageRow { ListJobId = listJobId, RecordedAtUtcTicks = Ticks(now), UnknownTypeCount = unknownTypeCount });
        var cursor = await db.Set<BillStreamCursorRow>().SingleOrDefaultAsync(
            row => row.SourceId == admission.SourceId, cancellationToken);
        if (cursor is null)
        {
            cursor = new BillStreamCursorRow { SourceId = admission.SourceId };
            db.Add(cursor);
        }
        cursor.PassAdmittedBills = admission.StartsPass ? admittedBills : cursor.PassAdmittedBills + admittedBills;
        cursor.UpdatedAtUtcTicks = Ticks(now);
        if (!admission.EndsWindow) return;

        var windowSettled = cursor.PassAdmittedBills == 0;
        cursor.PassAdmittedBills = 0;
        if (windowSettled && (cursor.LastProcessedDay is null || cursor.LastProcessedDay < admission.Window.LastDay))
            cursor.LastProcessedDay = admission.Window.LastDay;
        if (windowSettled && !admission.Window.IsBehind(now)) return;
        _ = await MakeScheduleDueAsync(db, admission.SourceId, now, cancellationToken);
    }

    private static Task<DateOnly?> ReadCursorAsync(CollectionAttemptDbContext db, string sourceId,
        CancellationToken cancellationToken) =>
        db.Set<BillStreamCursorRow>().Where(row => row.SourceId == sourceId)
            .Select(row => row.LastProcessedDay).SingleOrDefaultAsync(cancellationToken);
}
