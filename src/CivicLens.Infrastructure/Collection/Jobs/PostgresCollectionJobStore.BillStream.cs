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

    public Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        BillStreamWindow window, CongressBillListPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(listJobId);
        return TransactionAsync(async (db, now) =>
        {
            var admission = source.CreateBillStreamAdmission(listJobId, window, page, DateOnly.FromDateTime(now.UtcDateTime));
            var result = await AdmitBillStreamJobsAsync(db, admission, now, cancellationToken);
            if (result.DeferredCount > 0)
                throw new InvalidOperationException($"Source '{source.SourceId}' admission policy cannot hold a full bill list page.");
            if (admission.EndsWindow)
                await AdvanceCursorAsync(db, admission, now, cancellationToken);
            return result;
        }, cancellationToken);
    }

    private static async Task<DiscoveryAdmissionResult> AdmitBillStreamJobsAsync(CollectionAttemptDbContext db,
        BillStreamAdmission admission, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var keys = admission.Jobs.Select(job => job.IdempotencyKey).ToArray();
        var knownKeys = (await db.Set<JobRow>().Where(row => keys.Contains(row.IdempotencyKey))
            .Select(row => row.IdempotencyKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var revisionWriter = new ConfigurationRevisionWriter(db, Ticks(now));
        var admitted = new List<DiscoveryAdmissionJob>();
        var deferred = 0;
        var requests = 0;
        long bytes = 0;
        var seconds = 0;
        foreach (var job in admission.Jobs.Where(job => !knownKeys.Contains(job.IdempotencyKey)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!admission.Policy.TryReserve(job.Definition, admitted.Count, requests, bytes, seconds, out var reservation))
            {
                deferred++;
                continue;
            }
            var jobId = Guid.NewGuid().ToString("N");
            db.Add(new JobRow
            {
                JobId = jobId,
                IdempotencyKey = job.IdempotencyKey,
                DefinitionJson = StoredJobDefinition.Write(job.Definition),
                ConfigurationRevisionId = await revisionWriter.StoreAsync(job.Definition.ConfigurationRevision, cancellationToken),
                State = CollectionJobState.Pending,
                CreatedAt = Ticks(now)
            });
            admitted.Add(new DiscoveryAdmissionJob(jobId, job.Definition.Url));
            if (admitted.Count % BillStreamSaveBatchSize == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
            requests = checked(requests + reservation.Requests);
            bytes = checked(bytes + reservation.Bytes);
            seconds = checked(seconds + reservation.TimeoutSeconds);
        }
        return new DiscoveryAdmissionResult(admitted, deferred, knownKeys.Count);
    }

    private static async Task AdvanceCursorAsync(CollectionAttemptDbContext db, BillStreamAdmission admission,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cursor = await db.Set<BillStreamCursorRow>().SingleOrDefaultAsync(
            row => row.SourceId == admission.SourceId, cancellationToken);
        if (cursor is null)
            db.Add(new BillStreamCursorRow
            {
                SourceId = admission.SourceId,
                LastProcessedDay = admission.Window.LastDay,
                UpdatedAtUtcTicks = Ticks(now)
            });
        else if (cursor.LastProcessedDay < admission.Window.LastDay)
        {
            cursor.LastProcessedDay = admission.Window.LastDay;
            cursor.UpdatedAtUtcTicks = Ticks(now);
        }
        if (!admission.Window.IsBehind(now)) return;
        var schedule = await db.Set<CollectionScheduleRow>().SingleOrDefaultAsync(
            row => row.SourceId == admission.SourceId, cancellationToken);
        if (schedule is not null) schedule.NextDueUtcTicks = Math.Min(schedule.NextDueUtcTicks, Ticks(now));
    }

    private static Task<DateOnly?> ReadCursorAsync(CollectionAttemptDbContext db, string sourceId,
        CancellationToken cancellationToken) =>
        db.Set<BillStreamCursorRow>().Where(row => row.SourceId == sourceId)
            .Select(row => (DateOnly?)row.LastProcessedDay).SingleOrDefaultAsync(cancellationToken);
}
