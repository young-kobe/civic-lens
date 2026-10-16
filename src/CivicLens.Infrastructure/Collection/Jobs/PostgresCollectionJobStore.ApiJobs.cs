using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    private const int ApiJobSaveBatchSize = 10;

    private sealed record AdmittedApiJob(ApiJob Job, string JobId);

    private static string HashIdentity(string identity) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

    private static async Task<(DiscoveryAdmissionResult Result, IReadOnlyList<AdmittedApiJob> Admitted)>
        AdmitApiJobsAsync(CollectionAttemptDbContext db, ApiJobBatch batch, int saveBatchSize,
            DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = batch.Jobs;
        var keys = jobs.Select(job => job.IdempotencyKey).ToArray();
        var knownKeys = (await db.Set<JobRow>().Where(row => keys.Contains(row.IdempotencyKey))
            .Select(row => row.IdempotencyKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var jobWriter = new JobRowWriter(db, Ticks(now));
        var budget = batch.Policy.OpenBudget(batch.Template);
        var admitted = new List<AdmittedApiJob>();
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
            admitted.Add(new AdmittedApiJob(job, row.JobId));
            if (admitted.Count % saveBatchSize == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        }
        var admittedJobs = admitted.Select(item => new DiscoveryAdmissionJob(item.JobId, item.Job.Definition.Url)).ToList();
        return (new DiscoveryAdmissionResult(admittedJobs, deferred, knownKeys.Count), admitted);
    }
}
