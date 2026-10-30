using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Parser;
using CivicLens.Host.Publication;

namespace CivicLens.Tests.Host;

public sealed class GlossaryTests
{
    private const string ApprovedWordingHash = "8BEB42EFCCDA4C5F17F027DBF240AFECD6273BA457F366CF915265D57C84C370";
    private const int ApprovedTermCount = 38;

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
    public void EveryTermKeyIsUniqueSoNoTwoTermsShareAnAnchorOrAPopover()
    {
        Assert.Equal(AllTerms.Count(), AllTerms.Select(term => term.Key).Distinct().Count());
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
    public async Task ATermIsAPopoverOnItsFirstUseOnlyAndPlainTextAfterThat()
    {
        var document = new HtmlParser().ParseDocument(await Renderer.RenderMethodAsync(CancellationToken.None));

        var targets = document.QuerySelectorAll("button.term").Select(button => button.GetAttribute("popovertarget")).ToList();

        Assert.Equal(targets.Count, targets.Distinct().Count());
        Assert.All(targets, target => Assert.NotNull(document.QuerySelector($"[popover]#{target}")));
        Assert.Equal(targets.Count, document.QuerySelectorAll("[popover]").Length);
        Assert.Equal(2, document.QuerySelector("main")!.TextContent.Split("Not Voting").Length - 1);
        Assert.Single(targets, "term-not-voting");
    }

    [Fact]
    public async Task ThePopoverOpensFromAButtonSoKeyboardAndTouchReadersNeedNoScriptAndTheDefinitionReadsOnTheGlossaryPageWithoutIt()
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
