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
    private const int BillStreamSaveBatchSize = 10;

    private sealed record AdmittedBillStreamJob(BillStreamJob Job, string JobId);

    public Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        BillStreamWindow window, CongressBillListPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(listJobId);
        return TransactionAsync(async (db, now) =>
        {
            var admission = source.CreateBillStreamAdmission(listJobId, window, page, DateOnly.FromDateTime(now.UtcDateTime));
            var (result, admitted) = await AdmitBillStreamJobsAsync(db, admission.Batch, BillStreamSaveBatchSize, now, cancellationToken);
            if (result.DeferredCount > 0)
                throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold a full bill list page.");
            var admittedBills = admitted.Count(item => item.Job != admission.NextPage);
            await RecordPassAsync(db, listJobId, admission, admittedBills, now, cancellationToken);
            return result;
        }, cancellationToken);
    }

    private static async Task<(DiscoveryAdmissionResult Result, IReadOnlyList<AdmittedBillStreamJob> Admitted)>
        AdmitBillStreamJobsAsync(CollectionAttemptDbContext db, BillStreamBatch batch, int saveBatchSize,
            DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = batch.Jobs;
        var keys = jobs.Select(job => job.IdempotencyKey).ToArray();
        var knownKeys = (await db.Set<JobRow>().Where(row => keys.Contains(row.IdempotencyKey))
            .Select(row => row.IdempotencyKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var jobWriter = new JobRowWriter(db, Ticks(now));
        var budget = batch.Policy.OpenBudget(batch.Template);
        var admitted = new List<AdmittedBillStreamJob>();
        var deferred = 0;
        foreach (var job in jobs.Where(job => !knownKeys.Contains(job.IdempotencyKey)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.TryReserve())
            {
                deferred++;
                continue;
            }
            var row = await jobWriter.AddJobAsync(job.IdempotencyKey, job.Definition, cancellationToken);
            admitted.Add(new AdmittedBillStreamJob(job, row.JobId));
            if (admitted.Count % saveBatchSize == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        }
        var admittedJobs = admitted.Select(item => new DiscoveryAdmissionJob(item.JobId, item.Job.Definition.Url)).ToList();
        return (new DiscoveryAdmissionResult(admittedJobs, deferred, knownKeys.Count), admitted);
    }

    private static async Task RecordPassAsync(CollectionAttemptDbContext db, string listJobId,
        BillStreamAdmission admission, int admittedBills, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Set<BillStreamPageRow>().AnyAsync(row => row.ListJobId == listJobId, cancellationToken)) return;
        db.Add(new BillStreamPageRow { ListJobId = listJobId, RecordedAtUtcTicks = Ticks(now) });
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
        var schedule = await db.Set<CollectionScheduleRow>().SingleOrDefaultAsync(
            row => row.SourceId == admission.SourceId, cancellationToken);
        if (schedule is not null) schedule.NextDueUtcTicks = Math.Min(schedule.NextDueUtcTicks, Ticks(now));
    }

    private static Task<DateOnly?> ReadCursorAsync(CollectionAttemptDbContext db, string sourceId,
        CancellationToken cancellationToken) =>
        db.Set<BillStreamCursorRow>().Where(row => row.SourceId == sourceId)
            .Select(row => row.LastProcessedDay).SingleOrDefaultAsync(cancellationToken);
}
