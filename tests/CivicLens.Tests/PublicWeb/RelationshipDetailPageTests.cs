using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using static CivicLens.Tests.PublicWeb.DetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class RelationshipDetailPageTests
{
    [Fact]
    public async Task TheReasonTheQuoteAndTheFactShowTogetherInTheMainColumnWithTheirExactText()
    {
        var data = Relationship();
        var page = await RenderAsync<RelationshipDetailPage, RelationshipPageData>(data);

        var main = page.QuerySelector(".detail-main")!;
        Assert.Equal(data.Reason, main.QuerySelector(".ai-summary p")!.TextContent);
        Assert.Equal([RelationshipPageView.QuoteLabel, "Fact of the bill or resolution"], main.QuerySelectorAll(".detail-quotes .choice-label").Select(label => label.TextContent));
        Assert.Equal([data.Quote, data.Fact], main.QuerySelectorAll(".detail-quotes blockquote").Select(quote => quote.TextContent));
        Assert.Null(main.QuerySelector(".ai-summary p b"));
    }

    [Fact]
    public async Task AModelReasonCarriesTheLabelOfTheViewAndThePersonApprovalCaveatAndAModelFactCarriesItsOwnLabel()
    {
        var page = await RenderAsync<RelationshipDetailPage, RelationshipPageData>(Relationship());

        var summary = page.QuerySelector(".ai-summary-ai")!;
        Assert.Equal(PublishedWording.ModelRelationship, summary.QuerySelector(".ai-summary-label")!.TextContent);
        Assert.Contains(RelationshipPageView.Caveat, summary.QuerySelector(".ai-summary-check")!.TextContent);
        Assert.Equal("#source", summary.QuerySelector(".ai-summary-check a")!.GetAttribute("href"));
        Assert.Equal("An AI summary of the official record", page.QuerySelectorAll(".detail-quotes li")[1].QuerySelector(".row span")!.TextContent);
        Assert.Null(page.QuerySelectorAll(".detail-quotes li")[0].QuerySelector(".row"));
    }

    [Fact]
    public async Task APersonReasonHasNoAiLabel()
    {
        var page = await RenderAsync<RelationshipDetailPage, RelationshipPageData>(Relationship(PublishedTextOrigin.Person) with { FactLabel = null });

        Assert.Null(page.QuerySelector(".ai-summary-ai"));
        Assert.Null(page.QuerySelector(".ai-summary-label"));
        Assert.Null(page.QuerySelector(".detail-quotes .row"));
    }

    [Fact]
    public async Task TheLedeAndTheFoldGiveTheExactStatementDateAndTheStoredTimesAndAnUndatedLinkSaysSo()
    {
        var dated = await RenderAsync<RelationshipDetailPage, RelationshipPageData>(Relationship(statementDate: new(2026, 2, 3)));
        var undated = await RenderAsync<RelationshipDetailPage, RelationshipPageData>(Relationship(dated: false));

        Assert.Equal("Said on 3 Feb 2026.", dated.QuerySelector(".detail-lede")!.TextContent);
        var facts = dated.QuerySelectorAll("#source dt").Zip(dated.QuerySelectorAll("#source dd"), (term, value) => $"{term.TextContent}={value.TextContent}");
        Assert.Equal(["Type=Describes own bill", "Statement date=3 Feb 2026", "Approved=30 Oct 2026, 00:42 UTC", "First published=30 Oct 2026, 00:44 UTC", "Revision=2"], facts);
        Assert.Equal("Date not stated.", undated.QuerySelector(".detail-lede")!.TextContent);
        Assert.DoesNotContain(undated.QuerySelectorAll("#source dt"), term => term.TextContent == "Statement date");
    }

    [Fact]
    public async Task EachLinkedRecordLinksItsPageWithItsLabelAndTheRevisionThatTheLinkCites()
    {
        var page = await RenderAsync<RelationshipDetailPage, RelationshipPageData>(Relationship());

        var rows = page.QuerySelectorAll("[aria-labelledby=relationship-linked-title] li");
        Assert.Equal([Root + PublicationProtocol.RecordPagePath(StatementId), Root + PublicationProtocol.RecordPagePath(ActionId)],
            rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")));
        Assert.Equal([$"{StatementLabel}|Statement, revision 2", "Bill or resolution, revision 1"],
            rows.Select(row => string.Join('|', row.QuerySelectorAll("p").Select(line => line.TextContent))));
        Assert.Equal("Relationship | Describes own bill", page.QuerySelector(".detail-eyebrow")!.TextContent);
        Assert.Equal("Mark Kelly", page.QuerySelector(".public-breadcrumbs [aria-current=page]")!.TextContent);
    }
}
