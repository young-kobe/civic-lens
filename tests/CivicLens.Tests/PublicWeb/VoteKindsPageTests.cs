using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Host.Publication;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static CivicLens.Tests.PublicWeb.VoteDetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class VoteKindsPageTests
{
    private const string IndexRoot = "https://civic-lens.test/index-root/";

    [Fact]
    public void TheTableWordsEveryVoteKindExceptOtherAndHoldsEachOnce()
    {
        var expected = Enum.GetValues<PublishedVoteKind>().Where(kind => kind != PublishedVoteKind.Other);

        Assert.Equal(expected.Order(), VoteMeaning.Kinds.Order());
        Assert.Equal(VoteMeaning.Kinds.Count, VoteMeaning.Kinds.Distinct().Count());
    }

    [Fact]
    public async Task EveryKindInTheTableShowsOnceInTheOrderOfTheTable()
    {
        var page = await RenderIndexAsync();

        var names = page.QuerySelectorAll(".vote-kind h2").Select(heading => heading.TextContent);

        Assert.Equal(VoteMeaning.Kinds.Select(PlainVoteWords.Kind), names);
        Assert.Equal(VoteMeaning.Kinds.Count, page.QuerySelectorAll(".vote-kind").Length);
    }

    [Fact]
    public async Task EachRowShowsTheTextOfItsOwnersAndTheGlossaryTermLink()
    {
        var page = await RenderIndexAsync();
        var rows = page.QuerySelectorAll(".vote-kind");

        for (var index = 0; index < rows.Length; index++)
        {
            var kind = VoteMeaning.Kinds[index];
            var term = Glossary.Term(PublishedRecordLabels.VoteKindTermKey(kind)!);
            var paragraphs = rows[index].QuerySelectorAll("p");
            var link = paragraphs[0].QuerySelector("a")!;

            Assert.Equal(PlainVoteWords.Kind(kind), rows[index].QuerySelector("h2")!.TextContent);
            Assert.Equal(term.Name, link.TextContent);
            Assert.Equal($"{IndexRoot}{PublicationProtocol.GlossaryPagePath}#{term.AnchorId}", link.GetAttribute("href"));
            Assert.Equal(VoteMeaning.YesOf(kind), paragraphs[1].TextContent);
            Assert.Equal(2, paragraphs.Length);
        }
    }

    [Fact]
    public async Task ThePageHoldsNoWordingOutsideTheOwnersTexts()
    {
        var page = await RenderIndexAsync();

        var owned = new[] { Glossary.KindsOfVotes }.Concat(VoteMeaning.Kinds.SelectMany(kind => new[]
        {
            PlainVoteWords.Kind(kind),
            Glossary.Term(PublishedRecordLabels.VoteKindTermKey(kind)!).Name,
            VoteMeaning.YesOf(kind)
        }));
        var shown = page.QuerySelector(".public-main")!.Descendants<IText>()
            .Select(text => text.Data.Trim()).Where(text => text.Length > 0);

        Assert.Equal(owned, shown);
    }

    [Fact]
    public async Task TheGlossaryPageLinksToTheIndexFromItsKindsOfVotesGroup()
    {
        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderGlossaryAsync(CancellationToken.None));

        var link = document.QuerySelector($"section[aria-labelledby='{GlossaryGroup.AnchorOf(Glossary.KindsOfVotes)}'] a[href='{PublicationProtocol.VoteKindsPagePath}']");

        Assert.Equal(Glossary.KindsOfVotes, link!.TextContent);
    }

    [Fact]
    public async Task TheVotePageLinksToTheIndexFromItsKindExplainer()
    {
        var page = await RenderAsync(Vote());

        var link = page.QuerySelector($".vote-explain a[href='{Root}{PublicationProtocol.VoteKindsPagePath}']");

        Assert.Equal(Glossary.KindsOfVotes, link!.TextContent);
    }

    private static async Task<IDocument> RenderIndexAsync()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<VoteKindsPage>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(VoteKindsPage.RootPath)] = IndexRoot }))).ToHtmlString());
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
