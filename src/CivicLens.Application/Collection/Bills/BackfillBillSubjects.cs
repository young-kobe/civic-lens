using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection.Bills;

public sealed record BillSubjectsBackfillResult(int Examined, int Admitted);

public sealed class BackfillBillSubjects(IBillSubjectsBackfillStore store, ICollectionJobStore jobs, ApiResponseReader responses,
    string artifactRoot)
{
    public async Task<BillSubjectsBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var detailJobIds = await store.ListNewestBillsWithoutSubjectCountAsync(limit, cancellationToken);
        var admitted = 0;
        foreach (var detailJobId in detailJobIds)
            if (await AdmitAsync(detailJobId, cancellationToken)) admitted++;
        return new(detailJobIds.Count, admitted);
    }

    private async Task<bool> AdmitAsync(string detailJobId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(detailJobId, cancellationToken) ?? throw new InvalidDataException("A bill detail job is missing.");
        var bill = CongressBillDetailJob.Parse(job, await responses.ReadAsync(job, artifactRoot, cancellationToken));
        var source = new ConfiguredCollectionSource(job.Definition.ConfigurationRevision!, job.Definition.SourceId, job.Definition.CoverageAsOf);
        return await store.AdmitSubjectsChainAsync(source, detailJobId, bill.SubjectCount, cancellationToken);
    }
}
