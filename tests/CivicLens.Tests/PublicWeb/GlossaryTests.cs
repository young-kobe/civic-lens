using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Parser;
using CivicLens.Host.Publication;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Tests.PublicWeb;

public sealed class GlossaryTests
{
    private const string ApprovedWordingHash = "AB978AC6639051D7E46D981C1CB754CE9655617A2CB203DE7E8E23216D34AEDE";
    private const int ApprovedTermCount = 44;

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
                text.AppendJoin('\n', term.Key, term.Name, term.Means, term.WhyItMatters, term.Source, term.LongForm, term.SourceHref).Append('\n');
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
            if (term.SourceHref is { } sourceHref)
            {
                var source = entry.QuerySelector("dd:last-of-type > a");
                Assert.NotNull(source);
                Assert.Equal(sourceHref, source.GetAttribute("href"));
                Assert.Equal(term.Source, source.TextContent);
            }
            else
                Assert.Empty(entry.QuerySelectorAll("dd:last-of-type a"));
        }
        Assert.Equal(AllTerms.Count() + 1, main.QuerySelectorAll("section.glossary-term").Length);
        Assert.Equal(Glossary.PartyCountRules, main.QuerySelectorAll("#glossary-party-count + ul li").Select(item => item.TextContent));
        Assert.Contains(Glossary.Term("cloture").LongForm!, main.TextContent);
    }

    [Fact]
    public async Task TheGlossaryIndexGroupsEveryTermAlphabeticallyAndLinksPopulatedLettersAndDefinitions()
    {
        var document = new HtmlParser().ParseDocument(await Renderer.RenderGlossaryAsync(CancellationToken.None));
        var main = document.QuerySelector("main")!;
        var index = main.QuerySelector($"nav[aria-label='{Glossary.IndexLabel}']")!;
        var entries = index.QuerySelectorAll(".glossary-index-terms a").ToList();
        var expected = AllTerms.Select(term => (term.Name, term.AnchorId))
            .Append((Name: Glossary.PartyCountHeading, AnchorId: Glossary.PartyCountAnchorId))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(expected.Select(entry => entry.Name), entries.Select(link => link.TextContent));
        Assert.Equal(expected.Select(entry => $"#{entry.AnchorId}"), entries.Select(link => link.GetAttribute("href")));
        Assert.All(entries, link => Assert.NotNull(main.QuerySelector($"[id='{link.GetAttribute("href")!.TrimStart('#')}']")));
        Assert.Equal(entries.Count, entries.Select(link => link.GetAttribute("href")).Distinct().Count());

        var letters = index.QuerySelectorAll(".glossary-index-letters a").ToList();
        var letterHeadings = index.QuerySelectorAll(".glossary-index-groups h3").ToList();
        var expectedLetters = expected.Select(entry => char.ToUpperInvariant(entry.Name[0]).ToString()).Distinct().ToList();
        Assert.Equal(expectedLetters, letterHeadings.Select(heading => heading.TextContent));
        Assert.Equal(expectedLetters, letters.Select(link => link.TextContent));
        Assert.Equal(letterHeadings.Select(heading => "#" + heading.Id), letters.Select(link => link.GetAttribute("href")));
        Assert.All(letterHeadings, heading => Assert.Equal("-1", heading.GetAttribute("tabindex")));
        Assert.All(index.QuerySelectorAll(".glossary-index-groups > li"), group =>
        {
            var letter = group.QuerySelector("h3")!.TextContent;
            var groupEntries = group.QuerySelectorAll(".glossary-index-terms a");
            Assert.NotEmpty(groupEntries);
            Assert.All(groupEntries, link => Assert.Equal(letter, char.ToUpperInvariant(link.TextContent[0]).ToString()));
        });
        Assert.All(Glossary.Groups, group => Assert.NotNull(main.QuerySelector($"[id='{group.AnchorId}']")));
        var ids = main.QuerySelectorAll("[id]").Select(element => element.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(index.CompareDocumentPosition(main.QuerySelector("section.glossary-term")!).HasFlag(AngleSharp.Dom.DocumentPositions.Following));
    }

    [Fact]
    public async Task ThePartyCountNoteKeepsItsBodyGroupAndIsInTheAlphabeticalIndex()
    {
        var document = new HtmlParser().ParseDocument(await Renderer.RenderGlossaryAsync(CancellationToken.None));
        var main = document.QuerySelector("main")!;
        var indexLink = main.QuerySelector($"nav a[href='#{Glossary.PartyCountAnchorId}']")!;
        var bodyPanel = main.QuerySelectorAll("section[aria-labelledby^='glossary-group-']")
            .Single(panel => panel.QuerySelector($"#{Glossary.PartyCountAnchorId}") is not null);

        Assert.Equal(Glossary.PartyCountHeading, indexLink.TextContent);
        Assert.Equal(Glossary.PartyCountGroup.AnchorId, bodyPanel.GetAttribute("aria-labelledby"));
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
