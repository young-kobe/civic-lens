using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection.Bills;

public sealed record BillStatusBackfillResult(int Examined, int Recorded, int Failed);

public sealed class BackfillBillStatus(IBillStatusBackfillStore store, ICollectionJobStore jobs, ApiResponseReader responses,
    string artifactRoot)
{
    public async Task<BillStatusBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var detailJobIds = await store.ListNewestBillsWithoutStatusAsync(limit, cancellationToken);
        int recorded = 0, failed = 0;
        foreach (var detailJobId in detailJobIds)
        {
            try
            {
                if (await RecordAsync(detailJobId, cancellationToken)) recorded++;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                failed++;
            }
        }
        return new(detailJobIds.Count, recorded, failed);
    }

    private async Task<bool> RecordAsync(string detailJobId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(detailJobId, cancellationToken) ?? throw new InvalidDataException("A bill detail job is missing.");
        var bill = CongressBillDetailJob.Parse(job, await responses.ReadAsync(job, artifactRoot, cancellationToken));
        return await store.RecordStatusAsync(detailJobId, bill.LatestAction, cancellationToken);
    }
}
