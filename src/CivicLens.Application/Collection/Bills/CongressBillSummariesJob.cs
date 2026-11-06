using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;

namespace CivicLens.Application.Collection.Bills;

/// <summary>The one owner of how a saved summaries page job becomes the summary texts of its bill. The action reader and the vote reader both use it.</summary>
public static class CongressBillSummariesJob
{
    public static async Task<(CongressBillListItem Bill, IReadOnlyList<CongressBillSummaryText> Texts)> ParseAsync(CollectionJobRecord job,
        ApiResponse response, IDocumentTextExtractor textExtractor, CancellationToken cancellationToken)
    {
        var source = new ConfiguredCollectionSource(job.Definition.ConfigurationRevision!, job.Definition.SourceId, job.Definition.CoverageAsOf);
        var bill = BillPageUrl.TryRead(source.SourceUrl, ApiResource.BillSummaries, job.Definition.Url)?.Bill
            ?? throw new InvalidDataException("Bill summaries job URL is not a page of the summaries resource.");
        var parsed = CongressBillSummariesParser.Parse(response.Body, response.Request, source.SourceUrl, bill);
        var texts = new List<CongressBillSummaryText>();
        foreach (var summary in parsed.Summaries)
            texts.Add(new CongressBillSummaryText(summary, await textExtractor.ExtractHtmlFragmentAsync(summary.Html, cancellationToken)));
        return (bill, texts);
    }
}
