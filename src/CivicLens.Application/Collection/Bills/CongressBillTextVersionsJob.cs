using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillTextVersionsJob
{
    public static (CongressBillListItem Bill, CongressBillTextVersionsPage Page) Parse(CollectionJobRecord job, ApiResponse response)
    {
        var source = new ConfiguredCollectionSource(job.Definition.ConfigurationRevision!, job.Definition.SourceId, job.Definition.CoverageAsOf);
        var bill = BillPageUrl.TryRead(source.SourceUrl, ApiResource.BillTextVersions, job.Definition.Url)?.Bill
            ?? throw new InvalidDataException("Bill text versions job URL is not a page of the text resource.");
        return (bill, CongressBillTextVersionsParser.Parse(response.Body, bill));
    }
}
