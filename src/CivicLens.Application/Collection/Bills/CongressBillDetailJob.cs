using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillDetailJob
{
    public static CongressBill Parse(CollectionJobRecord job, ApiResponse response)
    {
        var source = new ConfiguredCollectionSource(job.Definition.ConfigurationRevision!, job.Definition.SourceId, job.Definition.CoverageAsOf);
        var expected = CongressBillListItem.TryReadDetailUrl(source.SourceUrl, job.Definition.Url)
            ?? throw new InvalidDataException("Bill detail job URL is not a bill detail URL.");
        return CongressBillParser.Parse(response.Body, expected);
    }
}
