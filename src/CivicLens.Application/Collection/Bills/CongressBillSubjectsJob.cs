using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillSubjectsJob
{
    public static (CongressBillListItem Bill, CongressBillSubjectsPage Page) Parse(CollectionJobRecord job, ApiResponse response)
    {
        var source = new ConfiguredCollectionSource(job.Definition.ConfigurationRevision!, job.Definition.SourceId, job.Definition.CoverageAsOf);
        var bill = BillPageUrl.TryRead(source.SourceUrl, ApiResource.BillSubjects, job.Definition.Url)?.Bill
            ?? throw new InvalidDataException("Bill subjects job URL is not a page of the subjects resource.");
        return (bill, CongressBillSubjectsParser.Parse(response.Body, response.Request, source.SourceUrl, bill));
    }
}
