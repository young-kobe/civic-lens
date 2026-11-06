using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Parser;
using CivicLens.Host.Publication;

namespace CivicLens.Tests.Host;

public sealed class GlossaryTests
{
    private const string ApprovedWordingHash = "897C46F1360F7E5E46B9220E91EAC9C48D327677A0E9014E67031AC0161DB33D";
    private const int ApprovedTermCount = 39;

    private static readonly ReleaseRenderer Renderer = new(null, null);

    private static IEnumerable<GlossaryTerm> AllTerms => Glossary.Groups.SelectMany(group => group.Terms);

    [Fact]
    public void TheApprovedWordingIsUnchangedBecauseOnlyTheOwnerMayApproveANewWord()
    {
        var text = new StringBuilder();
        foreach (var group in Glossary.Groups)
        {
            text.Append(group.Title).Append('\n');
            foreach (var term in group.Terms)
                text.AppendJoin('\n', term.Key, term.Name, term.Means, term.WhyItMatters, term.Source, term.LongForm).Append('\n');
        }
        text.Append(Glossary.PartyCountHeading).Append('\n').AppendJoin('\n', Glossary.PartyCountRules);

        Assert.Equal(ApprovedTermCount, AllTerms.Count());
        Assert.Equal(ApprovedWordingHash, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
    }

    [Fact]
    public async Task TheGlossaryPageListsEveryTermWithItsMeaningReasonAndSourceAndEveryPartyCountRule()
    {
        var document = new HtmlParser().ParseDocument(await Renderer.RenderGlossaryAsync(CancellationToken.None));
        var main = document.QuerySelector("main")!;

        foreach (var term in AllTerms)
        {
            var entry = main.QuerySelector($"section[aria-labelledby='{term.AnchorId}']");
            Assert.NotNull(entry);
            Assert.Equal(term.Name, entry.QuerySelector("h3")!.TextContent);
            var text = entry.TextContent;
            Assert.Contains(term.Means, text);
            Assert.Contains(term.WhyItMatters, text);
            Assert.Contains(term.Source, text);
        }
        Assert.Equal(AllTerms.Count() + 1, main.QuerySelectorAll("section.glossary-term").Length);
        Assert.Equal(Glossary.PartyCountRules, main.QuerySelectorAll("#glossary-party-count + ul li").Select(item => item.TextContent));
        Assert.Contains(Glossary.Term("cloture").LongForm!, main.TextContent);
    }

    [Fact]
    public async Task TheGlossaryPageStartsWithAnIndexThatLinksEveryGroupAndTermToItsAnchor()
    {
        var document = new HtmlParser().ParseDocument(await Renderer.RenderGlossaryAsync(CancellationToken.None));
        var main = document.QuerySelector("main")!;
        var index = main.QuerySelector($"nav[aria-label='{Glossary.IndexLabel}']")!;

        var expected = Glossary.Groups.Select(group => group.AnchorId)
            .Concat(AllTerms.Select(term => term.AnchorId))
            .Append(Glossary.PartyCountAnchorId);
        var links = index.QuerySelectorAll("a").ToList();

        Assert.Equal(expected.Order(), links.Select(link => link.GetAttribute("href")!.TrimStart('#')).Order());
        Assert.All(links, link => Assert.NotNull(main.QuerySelector($"[id='{link.GetAttribute("href")!.TrimStart('#')}']")));
        Assert.True(index.CompareDocumentPosition(main.QuerySelector("section.glossary-term")!).HasFlag(AngleSharp.Dom.DocumentPositions.Following));
        Assert.Empty(index.QuerySelectorAll("h1, h2, h3, h4, h5, h6"));
    }

    [Fact]
    public async Task ThePartyCountNoteSitsInTheSameGroupInTheIndexAndInTheBody()
    {
        var document = new HtmlParser().ParseDocument(await Renderer.RenderGlossaryAsync(CancellationToken.None));
        var main = document.QuerySelector("main")!;
        var expected = Glossary.PartyCountGroup.AnchorId;

        var indexLink = main.QuerySelector($"nav a[href='#{Glossary.PartyCountAnchorId}']")!;
        var indexGroup = indexLink.Closest("ul.glossary-index-groups > li")!.QuerySelector("a")!;
        var bodyPanel = main.QuerySelectorAll("section[aria-labelledby^='glossary-group-']")
            .Single(panel => panel.QuerySelector($"#{Glossary.PartyCountAnchorId}") is not null);

        Assert.Equal($"#{expected}", indexGroup.GetAttribute("href"));
        Assert.Equal(expected, bodyPanel.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public async Task ATermIsAPopoverOnItsFirstUseOnlyAndPlainTextAfterThat()
    {
        var document = new HtmlParser().ParseDocument(await Renderer.RenderMethodAsync(CancellationToken.None));

        var targets = document.QuerySelectorAll("button.term").Select(button => button.GetAttribute("popovertarget")).ToList();

        Assert.Equal(targets.Count, targets.Distinct().Count());
        Assert.All(targets, target => Assert.NotNull(document.QuerySelector($"[popover]#{target}")));
        Assert.Equal(targets.Count, document.QuerySelectorAll("main [popover]").Length);
        Assert.Equal(2, document.QuerySelector("main")!.TextContent.Split("Not voting").Length - 1);
        Assert.Single(targets, "term-not-voting");
    }

    [Fact]
    public async Task TheMethodPageExplainsPresidentialNoticesWithThePresidentialDefinitionNotTheAgencyOne()
    {
        var document = new HtmlParser().ParseDocument(await Renderer.RenderMethodAsync(CancellationToken.None));

        var notices = document.QuerySelectorAll("button.term").Single(button => button.TextContent == "notices");

        Assert.Equal("term-presidential-notice", notices.GetAttribute("popovertarget"));
    }

    [Fact]
    public async Task ATermIsAButtonWithAPopoverHoldingTheApprovedMeaningAndTheFooterLinksToTheGlossaryPage()
    {
        var document = new HtmlParser().ParseDocument(await Renderer.RenderAboutAsync(CancellationToken.None));

        var button = document.QuerySelector("button.term")!;
        var popover = document.QuerySelector($"#{button.GetAttribute("popovertarget")}")!;

        Assert.Equal("button", button.GetAttribute("type"));
        Assert.True(popover.HasAttribute("popover"));
        Assert.Contains(Glossary.Term("federal-register").Means, popover.TextContent);
        Assert.Contains("glossary.html", document.QuerySelector("footer")!.InnerHtml);
    }

    [Fact]
    public void AnUnknownTermKeyFailsLoudlyInsteadOfShowingNothing()
    {
        Assert.Throws<ArgumentException>(() => Glossary.Term("nope"));
    }
}
