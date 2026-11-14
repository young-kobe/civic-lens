using AngleSharp.Dom;
using AngleSharp.Html.Parser;
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
    public async Task EveryKindInTheTableShowsOnceInTheOrderOfTheTableAndOtherComesLast()
    {
        var page = await RenderIndexAsync();

        var names = page.QuerySelectorAll("tbody tr").Select(row => row.QuerySelector("th, td")!.TextContent);

        Assert.Equal(VoteMeaning.Kinds.Select(PlainVoteWords.Kind).Append(PlainVoteWords.OtherKind), names);
    }

    [Fact]
    public async Task TheIndexShowsTheApprovedIntroAndColumnLabelsAndUsesTheIntroAsItsDescription()
    {
        var page = await RenderIndexAsync();

        Assert.Equal("Congress votes on many kinds of questions. This page lists each kind and says what a Yes vote on it means.", page.QuerySelector(".page-header-lede")!.TextContent);
        Assert.Equal(["Kind of vote", "Word in the record", "What a Yes vote means"], page.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.Equal(page.QuerySelector(".page-header-lede")!.TextContent, page.QuerySelector("meta[name=description]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task EachRowShowsTheTextOfItsOwnersAndTheGlossaryTermLink()
    {
        var page = await RenderIndexAsync();
        var rows = page.QuerySelectorAll("tbody tr");

        for (var index = 0; index < VoteMeaning.Kinds.Count; index++)
        {
            var kind = VoteMeaning.Kinds[index];
            var term = Glossary.Term(PublishedRecordLabels.VoteKindTermKey(kind)!);
            var cells = rows[index].Children;
            var link = cells[1].QuerySelector("a")!;

            Assert.Equal(PlainVoteWords.Kind(kind), cells[0].TextContent);
            Assert.Equal(PlainVoteWords.RecordTerm(kind), link.TextContent);
            Assert.Equal($"{IndexRoot}{PublicationProtocol.GlossaryPagePath}#{term.AnchorId}", link.GetAttribute("href"));
            Assert.Equal(VoteMeaning.YesOnIndexOf(kind), cells[2].TextContent);
            Assert.Equal(3, cells.Length);
        }
    }

    [Fact]
    public async Task TheRecordTermCanDifferFromItsGlossaryTitleWithoutChangingTheGlossaryTarget()
    {
        var page = await RenderIndexAsync();
        var passageRow = page.QuerySelectorAll("tbody tr").Single(row => row.Children[0].TextContent == PlainVoteWords.Kind(PublishedVoteKind.Passage));
        var link = passageRow.Children[1].QuerySelector("a")!;
        var term = Glossary.Term(PublishedRecordLabels.VoteKindTermKey(PublishedVoteKind.Passage)!);

        Assert.Equal("Passage or agreement", link.TextContent);
        Assert.NotEqual(term.Name, link.TextContent);
        Assert.Equal($"{IndexRoot}{PublicationProtocol.GlossaryPagePath}#{term.AnchorId}", link.GetAttribute("href"));
    }

    [Fact]
    public async Task TheOtherRowSendsTheReaderToTheVotePageAndHasNoGlossaryLink()
    {
        var page = await RenderIndexAsync();

        var cells = page.QuerySelectorAll("tbody tr").Last().Children;

        Assert.Equal("The record states the question in its own words. Read it on the vote page.", cells[2].TextContent);
        Assert.Equal(PlainVoteWords.OtherKind, cells[0].TextContent);
        Assert.Empty(cells[1].TextContent);
        Assert.Empty(cells[1].QuerySelectorAll("a"));
    }

    [Fact]
    public async Task ThePageHoldsNoWordingOutsideTheOwnersTexts()
    {
        var page = await RenderIndexAsync();

        var owned = new[] { Glossary.KindsOfVotes, Glossary.VoteKindsIntro, PublicColumn.KindOfVote, PublicColumn.WordInTheRecord, PublicColumn.WhatAYesVoteMeans }
            .Concat(VoteMeaning.Kinds.SelectMany(kind => new[]
            {
                PlainVoteWords.Kind(kind),
                PlainVoteWords.RecordTerm(kind)!,
                VoteMeaning.YesOnIndexOf(kind)
            }))
            .Concat([PlainVoteWords.OtherKind, VoteMeaning.OtherOnIndex]);
        var shown = page.QuerySelector(".public-main")!.Descendants<IText>()
            .Select(text => text.Data.Trim()).Where(text => text.Length > 0);

        Assert.Equal(owned.Distinct().Order(), shown.Distinct().Order());
    }

    [Fact]
    public void OnlyTheIndexFormOfAYesMeaningDropsTheQuestionWordsAndTheVotePagesKeepTheirText()
    {
        Assert.Equal("A Yes vote supported the action under a procedure with limited debate.", VoteMeaning.YesOnIndexOf(PublishedVoteKind.Suspension));
        Assert.Equal("A Yes vote supported passing or agreeing to the bill or measure.", VoteMeaning.YesOnIndexOf(PublishedVoteKind.Passage));
        Assert.Equal("A Yes vote supported beginning consideration of the bill or measure.", VoteMeaning.YesOnIndexOf(PublishedVoteKind.MotionToProceed));
        Assert.All(VoteMeaning.Kinds, kind => Assert.DoesNotContain("named in the question", VoteMeaning.YesOnIndexOf(kind)));
    }

    [Fact]
    public async Task TheKindsOfVotesPanelOfTheGlossaryLinksTheIndexAtItsPublicPathWithTheApprovedText()
    {
        var page = await RenderAtRootAsync<PublishedGlossaryPage>();
        var kindsPanel = Glossary.Groups.Single(group => group.Title == Glossary.KindsOfVotes).AnchorId;

        var link = Assert.Single(page.QuerySelectorAll($"a[href$='{PublicationProtocol.VoteKindsPagePath}']"));

        Assert.Equal(("See what a Yes vote means for each kind of vote", IndexRoot + "vote-kinds.html"), (link.TextContent, link.GetAttribute("href")));
        Assert.Equal(kindsPanel, link.Closest("section[aria-labelledby]")!.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public async Task TheVotePageLinksTheIndexAtItsPublicPathWithTheApprovedTextUnderItsYesMeaning()
    {
        var page = await RenderAsync(Vote());

        var link = Assert.Single(page.QuerySelectorAll($"a[href$='{PublicationProtocol.VoteKindsPagePath}']"));

        Assert.Equal(("See what a Yes vote means for each kind of vote", Root + "vote-kinds.html"), (link.TextContent, link.GetAttribute("href")));
        Assert.NotNull(link.Closest(".vote-explain"));
    }

    private static Task<IDocument> RenderIndexAsync() => RenderAtRootAsync<VoteKindsPage>();

    private static Task<IDocument> RenderAtRootAsync<TPage>() where TPage : IComponent =>
        ListPageRender.RenderPageAsync<TPage>(new Dictionary<string, object?> { [nameof(VoteKindsPage.RootPath)] = IndexRoot });

}
