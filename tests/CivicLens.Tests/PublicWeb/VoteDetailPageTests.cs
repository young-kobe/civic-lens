using System.Text.RegularExpressions;
using CivicLens.Tests.Fixtures;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using static CivicLens.Tests.PublicWeb.VoteDetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class VoteDetailPageTests
{
    [Fact]
    public async Task TheAnswerComesFirstThenTheResultTheExplainersThePartyBarsAndTheMembers()
    {
        var page = await RenderAsync(Vote());

        var order = new[] { "h1.vote-headline", ".ai-summary", ".vote-result", ".vote-zip", ".vote-explain", ".vote-parties", ".vote-breakers", ".vote-members" }
            .Select(selector => Position(page, selector)).ToArray();

        Assert.All(order, position => Assert.True(position >= 0));
        Assert.Equal(order.Order(), order);
        Assert.Equal("The House passed H.R. 5334.", page.QuerySelector("h1")!.TextContent);
    }

    [Fact]
    public async Task ACodeWrittenVoteShowsItsCodeLabelAndNoSummaryBoxBecauseTheSummaryOnlyRepeatsTheHeadline()
    {
        var page = await RenderAsync(Vote() with { TextOrigin = PublishedTextOrigin.Code, Label = AiLabelWords.CodeWording, Summary = "The House passed H.R. 5334." });

        Assert.Null(page.QuerySelector(".ai-summary"));
        Assert.Equal(AiLabelWords.CodeWording, page.QuerySelector(".vote-head .ai-label")!.TextContent);
        Assert.Equal(1, page.QuerySelector(".vote-head")!.TextContent.Split("The House passed H.R. 5334.").Length - 1);
    }

    [Fact]
    public async Task ASenateVoteWithCodeTextShowsTheSenateLabelOfTheView()
    {
        var page = await RenderAsync(Vote() with { Chamber = PublishedChamber.Senate, TextOrigin = PublishedTextOrigin.Code, Label = AiLabelWords.SenateCodeWording });

        Assert.Equal(AiLabelWords.SenateCodeWording, page.QuerySelector(".vote-head .ai-label")!.TextContent);
    }

    [Fact]
    public async Task TheAboutLineIsTheWholeBillLineWithItsSourceNoteRightUnderTheHeadline()
    {
        var page = await RenderAsync(Vote());

        var about = page.QuerySelector(".vote-head .vote-about")!;
        Assert.Equal("A bill line. " + PublishedWording.BillLineSource, about.TextContent);
        Assert.True(Position(page, "h1.vote-headline") < Position(page, ".vote-about"));
        Assert.DoesNotContain("A bill line.", page.QuerySelector(".vote-bill")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABillWithNoLineGivesTheSourceDescriptionWordForWordInItsPlace()
    {
        const string description = "Vote title: To strike section 2 of the bill.";
        var vote = Vote() with
        {
            Bill = Vote().Bill! with { Line = null },
            Facts = [.. Vote().Facts, new() { Text = description, Citation = Cite(Lines[1]) }]
        };

        var page = await RenderAsync(vote);

        Assert.Equal("To strike section 2 of the bill.", page.QuerySelector(".vote-about")!.TextContent);
    }

    [Fact]
    public async Task AVoteWithNoWholeBillLineAndNoDescriptionHasNoAboutLine()
    {
        var page = await RenderAsync(Vote() with { Bill = Vote().Bill! with { Line = null } });

        Assert.Null(page.QuerySelector(".vote-about"));
    }

    [Fact]
    public void ADescriptionThatTheHeadlineAlreadyHoldsIsNotShownTwice()
    {
        var vote = Vote() with
        {
            Bill = null,
            Headline = "The Senate voted on a nomination: Jane Doe, to be Secretary of Labor. The nominee was confirmed.",
            Facts = [.. Vote().Facts, new() { Text = "Vote title: Jane Doe, to be Secretary of Labor", Citation = Cite(Lines[1]) }]
        };

        Assert.Null(VotePageView.About(vote));
    }

    [Fact]
    public void AWholeBillLineComesBeforeTheDescriptionInTheAboutOrder()
    {
        var vote = Vote() with { Facts = [.. Vote().Facts, new() { Text = "Vote title: A purpose.", Citation = Cite(Lines[1]) }] };

        Assert.Equal(new VoteAbout("A bill line.", FromBill: true), VotePageView.About(vote));
        Assert.Equal(new VoteAbout("A purpose.", FromBill: false), VotePageView.About(vote with { Bill = null }));
    }

    [Fact]
    public async Task EveryPartyGroupGetsOneBarWithItsWholePartyCountsAndNoMarkAndTheChamberBarIsNeutral()
    {
        var page = await RenderAsync(Vote());

        var bars = page.QuerySelectorAll(".vote-parties .party-bar");
        Assert.Equal(["House Democrats", "House Republicans", "House Independents"], bars.Select(bar => bar.QuerySelector("figcaption")!.TextContent));
        Assert.Equal(["democrat", "republican", "neutral"], bars.Select(bar => bar.QuerySelector(".party-bar-fill[data-fill=solid]")!.GetAttribute("data-tone")));
        Assert.Equal("House Democrats: Yes 1, No 3, Not voting 1", bars[0].QuerySelector("svg")!.GetAttribute("aria-label"));
        Assert.All(bars, bar => Assert.Null(bar.QuerySelector("line.party-bar-mark")));
        Assert.All(bars, bar => Assert.DoesNotContain("needed", bar.TextContent, StringComparison.Ordinal));
        var chamber = page.QuerySelector(".vote-result .party-bar")!;
        Assert.Equal("neutral", chamber.QuerySelector(".party-bar-fill[data-fill=solid]")!.GetAttribute("data-tone"));
        Assert.Equal("House members: Yes 6, No 4, Present 1, Not voting 3", chamber.QuerySelector("svg")!.GetAttribute("aria-label"));
        Assert.Contains("14 members have a recorded position, including 3 who did not vote.", page.QuerySelector(".vote-result")!.TextContent);
        Assert.Contains("on 16 Sep 2026", page.QuerySelector(".vote-result")!.TextContent);
        Assert.Contains("Yes, No, Present, or Not voting", page.QuerySelector(".vote-result")!.TextContent);
    }

    [Theory]
    [InlineData(PublishedVoteKind.Passage, "more than half", 6)]
    [InlineData(PublishedVoteKind.Rule, "more than half", 6)]
    [InlineData(PublishedVoteKind.Nomination, "more than half", 6)]
    [InlineData(PublishedVoteKind.Suspension, "two-thirds", 7)]
    [InlineData(PublishedVoteKind.VetoOverride, "two-thirds", 7)]
    [InlineData(PublishedVoteKind.Cloture, null, 0)]
    [InlineData(PublishedVoteKind.Other, null, 0)]
    public async Task EachVoteKindDrawsOnlyTheMarkItNeedsOnTheChamberBarAndNoPartyBarHasAMark(PublishedVoteKind kind, string? share, int count)
    {
        var page = await RenderAsync(Vote() with { Kind = kind, KindCitation = null });

        var chamber = page.QuerySelector(".vote-result .party-bar")!;
        Assert.Equal(share is not null, chamber.QuerySelector("line.party-bar-mark") is not null);
        foreach (var other in new[] { "more than half", "two-thirds" })
            Assert.Equal(other == share, chamber.TextContent.Contains($"Yes votes needed: {other} of the 10 Yes and No votes", StringComparison.Ordinal));
        Assert.Equal(count, VotePageView.ChamberBar(Vote() with { Kind = kind, KindCitation = null })!.Mark?.Count ?? 0);
        Assert.Null(page.QuerySelector(".vote-parties line.party-bar-mark"));
    }

    [Fact]
    public async Task APassageThatMayAmendTheConstitutionDrawsNoHalfLineOnTheChamberBar()
    {
        var vote = Vote();
        var title = new PublishedVoteFact { Text = PublishedVote.TitlePrefix + "Proposing an amendment to the Constitution of the United States.", Citation = vote.Facts[0].Citation };

        var page = await RenderAsync(vote with { Facts = [.. vote.Facts, title] });

        Assert.Null(page.QuerySelector(".vote-result .party-bar line.party-bar-mark"));
    }

    [Fact]
    public async Task TheLegendLineStatesTheCountWhereTheLineIsDrawn()
    {
        var page = await RenderAsync(Vote());

        var chamber = page.QuerySelector(".vote-result .party-bar")!;
        var line = chamber.QuerySelector("svg.party-bar-chart line.party-bar-mark")!;
        Assert.Equal("42.86%", line.GetAttribute("x1"));
        Assert.Equal("6 Yes votes needed: more than half of the 10 Yes and No votes", Regex.Replace(chamber.QuerySelectorAll(".party-bar-legend li").Last().TextContent, @"\s+", " ").Trim());
    }

    [Fact]
    public async Task UnknownPartyCountsShowNoPartyCardInsteadOfZerosAndTheBreakersStillComeFromTheirPartyLines()
    {
        var page = await RenderAsync(Vote() with { PartyTallies = null });

        Assert.Null(page.QuerySelector(".vote-parties"));
        Assert.Equal(["Ada Able", "Di Dole"], page.QuerySelectorAll(".vote-breakers li a").Select(link => link.TextContent));
        Assert.Equal("This record does not show party counts.", page.QuerySelector(".vote-parties-unknown")!.TextContent.Trim());
        Assert.Single(page.QuerySelectorAll(".party-bar"));
    }

    [Fact]
    public async Task UnknownChamberTotalsShowTheResultWordWithoutABar()
    {
        var page = await RenderAsync(Vote() with { Totals = null });

        Assert.Equal("Passed Split vote", page.QuerySelector(".vote-result-word")!.TextContent);
        Assert.Null(page.QuerySelector(".vote-result .party-bar"));
    }

    [Fact]
    public async Task BreakersAreTheTrackedMembersWhosePartyLineBreaksAndEachPartyHeadingLinksToItsFullBoard()
    {
        var page = await RenderAsync(Vote());

        var groups = page.QuerySelectorAll(".vote-breaker-group");
        Assert.Equal(2, groups.Length);
        Assert.Equal("House Democrats: 1 tracked member voted against most of the party", groups[0].QuerySelector("h3")!.TextContent);
        Assert.Equal(["Ada Able"], groups[0].QuerySelectorAll("li a").Select(link => link.TextContent));
        Assert.Equal(Root + "board-house-democrat.html", groups[0].QuerySelector("h3 a")!.GetAttribute("href"));
        Assert.Equal(["Di Dole"], groups[1].QuerySelectorAll("li a").Select(link => link.TextContent));
    }

    [Fact]
    public async Task ABreakListOfMoreThanTwentyNamesStartsFoldedAndEachNameIsAListLink()
    {
        var vote = Vote();
        var ada = vote.Members.Single(member => member.Name == "Ada Able");
        var breakers = Enumerable.Range(0, 21).Select(index => ada with { OfficialId = $"b{index}", Name = $"Breaker {index}" });
        var page = await RenderAsync(vote with { Members = [.. vote.Members.Where(member => member != ada), .. breakers] });

        var democrats = page.QuerySelectorAll(".vote-breaker-group")[0];
        var fold = democrats.QuerySelector("details.member-fold")!;
        Assert.Null(fold.GetAttribute("open"));
        Assert.Equal("Show all 21 members", fold.QuerySelector("summary")!.TextContent);
        Assert.Equal(21, fold.QuerySelectorAll("ul.member-names > li > a").Length);
        Assert.Null(page.QuerySelectorAll(".vote-breaker-group")[1].QuerySelector("details"));
    }

    [Fact]
    public async Task AGroupWithNoBreakerShowsOneLineWithItsBoardLinkAndNoHeading()
    {
        var vote = Vote();
        var page = await RenderAsync(vote with { Members = [.. vote.Members.Where(member => member.Name != "Di Dole")] });

        var groups = page.QuerySelectorAll(".vote-breaker-group");
        Assert.Equal(2, groups.Length);
        Assert.NotNull(groups[0].QuerySelector("h3"));
        Assert.Null(groups[1].QuerySelector("h3"));
        Assert.Null(groups[1].QuerySelector("ul"));
        Assert.Equal("House Republicans: no tracked member voted against most of the party.", groups[1].TextContent.Trim());
        Assert.Equal(Root + "board-house-republican.html", groups[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.DoesNotContain("0 tracked", page.QuerySelector(".vote-breakers")!.TextContent);
    }

    [Fact]
    public async Task AVoteWhereThePartiesAgreedHasNoBreakersAndShowsItsBadge()
    {
        var vote = Vote();
        var page = await RenderAsync(vote with { Alignment = PublishedVoteAlignment.PartiesAgree, Members = [.. vote.Members.Select(member => member with { BreaksParty = null })] });

        Assert.Null(page.QuerySelector(".vote-breakers"));
        Assert.Equal(VoteWords.PartiesAgreedBadge, page.QuerySelector(".vote-result .badge")!.TextContent);
    }

    [Fact]
    public async Task TheMemberTableIsFoldedAndFiltersByNameVoteAndParty()
    {
        var all = await RenderAsync(Vote());
        var byCast = await RenderAsync(Vote(), new(Cast: "Nay"));
        var byName = await RenderAsync(Vote(), new(Name: "dole"));
        var byParty = await RenderAsync(Vote(), new(Party: "Republican", Cast: "Aye"));
        var none = await RenderAsync(Vote(), new(Name: "zzz"));

        Assert.Null(all.QuerySelector(".vote-members details")!.GetAttribute("open"));
        Assert.Equal(5, Names(all).Length);
        Assert.Equal(["Ben Baker", "Di Dole"], Names(byCast));
        Assert.Equal(["Di Dole"], Names(byName));
        Assert.Equal(["Cy Cole"], Names(byParty));
        Assert.NotNull(byCast.QuerySelector(".vote-members details")!.GetAttribute("open"));
        Assert.Contains("2 of 5 tracked members shown.", byCast.QuerySelector(".vote-members")!.TextContent);
        Assert.Contains("No tracked member matches these filters.", none.QuerySelector(".vote-members")!.TextContent);
    }

    [Fact]
    public async Task TheMembersCardFollowsTheResultWithTheApprovedHeadingAndPromptAndHoldsNoZipOrMemberData()
    {
        var house = await RenderAsync(Vote());
        var senate = await RenderAsync(Vote() with { Chamber = PublishedChamber.Senate });

        var card = house.QuerySelector(".vote-zip")!;
        Assert.Same(house.QuerySelector(".vote-result")!.NextElementSibling, card);
        Assert.Equal("Your members on this vote", card.QuerySelector("h2")!.TextContent);
        Assert.Equal(RecordId, card.GetAttribute("data-record-id"));
        Assert.Equal(Vote().Release.ToString(System.Globalization.CultureInfo.InvariantCulture), card.GetAttribute("data-release"));
        Assert.Equal("Enter your ZIP to see how your House member voted. Find my members", card.QuerySelector("[data-vote-members-prompt]")!.TextContent);
        Assert.Equal(Root + "index.html#zip-results", card.QuerySelector("[data-vote-members-prompt] a")!.GetAttribute("href"));
        Assert.Equal("Enter your ZIP to see how your senators voted. Find my members", senate.QuerySelector("[data-vote-members-prompt]")!.TextContent);
        Assert.Equal(0, card.QuerySelector("[data-vote-members-body]")!.ChildElementCount);
        Assert.Null(house.QuerySelector("[data-zcta]"));
        Assert.DoesNotContain(Vote().Members, member => card.TextContent.Contains(member.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void TheScriptFillsTheVoteCardFromThePrivateVoteFragmentOfThePageReleaseOnly()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.PublicWeb", "wwwroot", "js", "public.js"));

        Assert.Contains("document.querySelectorAll(\"[data-vote-members]\").forEach(card => initRecordMembers(rememberedMembers, card, membersStrip, \"vote\"));", script, StringComparison.Ordinal);
        Assert.Contains("${api}/v1/zip-districts/${zip}/${kind}s/", script, StringComparison.Ordinal);
        Assert.Contains("if (fragment.dataset.release !== card.dataset.release) { clear(); return; }", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFinderFiltersWhileTypingOnlyWhenTheTableHoldsEveryMemberAndEachRowCarriesItsCastAndPartyKeys()
    {
        var vote = Vote() with { Members = [.. Members, new("o6", "Fay Fox", "Not Voting", null, Cite(Lines[4]))] };

        var all = await RenderAsync(vote);
        var filtered = await RenderAsync(vote, new(Cast: "Nay"));

        Assert.NotNull(all.QuerySelector("form.vote-filter")!.GetAttribute("data-member-finder"));
        Assert.Null(filtered.QuerySelector("form.vote-filter")!.GetAttribute("data-member-finder"));
        Assert.Equal(VoteMemberFilter.NoMatch, all.QuerySelector("[data-member-finder-status]")!.GetAttribute("data-no-match"));
        var keys = all.QuerySelectorAll(".vote-members tbody tr").ToDictionary(row => row.QuerySelector("td:first-child")!.TextContent, row => row.GetAttribute("data-filter-keys"));
        Assert.Equal("cast:Aye party:Democrat", keys["Ada Able"]);
        Assert.Equal("cast:Not%20Voting party:none", keys["Fay Fox"]);
        var castValues = all.QuerySelectorAll("#vote-filter-cast option").Select(option => option.GetAttribute("value")).Where(value => value != "");
        Assert.All(castValues, value => Assert.Contains(keys.Values, key => key!.StartsWith($"cast:{Uri.EscapeDataString(value!)} ", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheFinderScriptMatchesTheNameCastAndPartyAsTheServerFilterDoesAndPagesTheMatchingRows()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.PublicWeb", "wwwroot", "js", "public.js"));

        Assert.Contains("document.querySelectorAll(\"form[data-member-finder]\").forEach(initMemberFinder);", script, StringComparison.Ordinal);
        Assert.Contains("(wanted !== \"\" && !row.cells[0].textContent.toLowerCase().includes(wanted))", script, StringComparison.Ordinal);
        Assert.Contains("(cast.value !== \"\" && rowKeys.get(\"cast\") !== cast.value)", script, StringComparison.Ordinal);
        Assert.Contains("(party.value !== \"\" && rowKeys.get(\"party\") !== party.value)", script, StringComparison.Ordinal);
        Assert.Contains("name.addEventListener(\"input\", apply);", script, StringComparison.Ordinal);
        Assert.Contains("form.addEventListener(\"submit\", event => { event.preventDefault(); apply(); });", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("forEach(initPaged);", StringComparison.Ordinal) < script.IndexOf("forEach(initMemberFinder);", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheMemberTableShowsEachCastThroughTheSharedCastBadgeWithItsSide()
    {
        var vote = Vote();
        vote = vote with { Members = [.. vote.Members, new("o7", "Gus Gray", "Present", null, Cite(Lines[4]))] };

        var page = await RenderAsync(vote);

        var rows = page.QuerySelectorAll(".vote-members tbody tr");
        Assert.Equal(vote.Members.Length, rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            var badge = rows[i].QuerySelector(".shared-vote-cast-value")!;
            var side = PublishedOfficialVote.SideOf(vote.Members[i].Cast)?.ToLowerInvariant();
            Assert.Equal(vote.Members[i].Name, rows[i].QuerySelector("td:first-child")!.TextContent);
            Assert.Equal(side, badge.GetAttribute("data-side"));
        }
        Assert.Null(rows[^1].QuerySelector(".shared-vote-cast-value")!.GetAttribute("data-side"));
    }

    [Fact]
    public async Task EachMemberRowLinksToItsMarkedLineInTheSourceFoldThatPublicJsOpens()
    {
        var page = await RenderAsync(Vote());

        var anchors = page.QuerySelectorAll(".vote-members .source-line").Select(link => link.GetAttribute("href")![1..]).ToArray();
        Assert.Equal(5, anchors.Length);
        var fold = page.QuerySelector("details.source-fold")!;
        Assert.All(anchors, anchor =>
        {
            var target = page.GetElementById(anchor)!;
            Assert.Contains("Member:", target.TextContent);
            Assert.Same(fold, target.Closest("details"));
        });
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.PublicWeb", "wwwroot", "js", "public.js"));
        Assert.Contains("closest(\"details\")", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener(\"hashchange\", openFoldOfHash)", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFilterFormAndTheClearLinkPointAtThePublicAddressOfTheVotePage()
    {
        var page = await RenderAsync(Vote(), new(Cast: "Nay"));

        Assert.Equal(PagePath + "#members", page.QuerySelector("form.vote-filter")!.GetAttribute("action"));
        Assert.Equal(PagePath + "#members", page.QuerySelector(".vote-filter-actions a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task ThePartyFilterOffersNoKnownPartyOnlyWhenAMemberHasNoPartyAndFindsThatMember()
    {
        var withoutParty = Vote() with { Members = [.. Members, new("o6", "Fay Fox", "Aye", null, Cite(Lines[4]))] };

        var offered = await RenderAsync(withoutParty, new(Party: VoteMemberFilter.NoKnownParty));
        var absent = await RenderAsync(Vote());

        var option = offered.QuerySelector("#vote-filter-party option[value=none]")!;
        Assert.Equal("No known party", option.TextContent);
        Assert.NotNull(option.GetAttribute("selected"));
        Assert.Equal(["Fay Fox"], Names(offered));
        Assert.Null(absent.QuerySelector("#vote-filter-party option[value=none]"));
    }

    [Fact]
    public async Task OneCheckTheSourceFoldHoldsTheCitedFactsAndTheSourceTextAndNoTimelineExists()
    {
        var page = await RenderAsync(Vote());

        var fold = Assert.Single(page.QuerySelectorAll("details.source-fold"));
        Assert.Equal("Check the source", fold.QuerySelector("summary")!.TextContent);
        Assert.Contains("Casts of all House members: Yes 6, No 4, Present 1, Not voting 3", fold.QuerySelector("dl.facts")!.TextContent);
        Assert.Equal(1, fold.QuerySelectorAll(".doc-text").Length);
        Assert.Null(page.QuerySelector(".timeline, #timeline-title"));
        Assert.DoesNotContain("Timeline", page.QuerySelector("main")!.TextContent);
    }

    [Fact]
    public async Task TheBillCardLinksTheBillAndIssueChipsLinkTheirPages()
    {
        var page = await RenderAsync(Vote());

        var bill = page.QuerySelector(".vote-bill")!;
        Assert.Equal(Root + "records/" + new string('b', 64) + ".html", bill.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal(Root + "issues/tax.html", page.QuerySelector(".vote-chip")!.GetAttribute("href"));
    }

    [Fact]
    public async Task ThePlainWordsOfTheApprovedWordingTableAnswerWhatYesMeantAndWhatHappensNext()
    {
        var page = await RenderAsync(Vote());

        var wording = VoteWording.Of(Vote().WordingSource)!.Value;
        var explain = page.QuerySelector(".vote-explain")!.TextContent;
        Assert.Contains(wording.Yes, explain);
        Assert.Contains(wording.No, explain);
        Assert.Contains(wording.NextStep, explain);
    }

    [Fact]
    public async Task AnUnsupportedQuestionShowsTheSharedUnknownNextStepSentence()
    {
        var page = await RenderAsync(Vote() with { KindCitation = null, Kind = PublishedVoteKind.Other });

        Assert.Contains(NextStep.NotShown, page.QuerySelector(".vote-explain")!.TextContent);
    }

    [Fact]
    public async Task TheSummaryKeepsItsAiLabelAndEscapesStoredText()
    {
        var page = await RenderAsync(Vote() with { Summary = "<script>x()</script>" });

        Assert.Equal(AiLabelWords.ModelWording, page.QuerySelector(".ai-label")!.TextContent);
        Assert.Equal("<script>x()</script>", page.QuerySelector(".ai-summary p")!.TextContent);
        Assert.Null(page.QuerySelector("main script"));
    }

    private static int Position(AngleSharp.Dom.IDocument page, string selector) =>
        page.QuerySelector(selector) is { } element ? page.All.ToList().IndexOf(element) : -1;

    private static string[] Names(AngleSharp.Dom.IDocument page) =>
        [.. page.QuerySelectorAll(".vote-members tbody tr td:first-child").Select(cell => cell.TextContent)];

    [Fact]
    public async Task TheCorrectionBannerFollowsTheHeadlineOnlyForAPersonCorrectionAndARoutineUpdateShowsTheMutedLineInTheHeader()
    {
        var corrected = await RenderAsync(Vote() with { Corrections = [PublishedCorrectionBannerTests.Newest] });
        var plain = await RenderAsync(Vote());
        var updated = await RenderAsync(Vote() with { Corrections = [PublishedUpdateLineTests.SourceUpdate] });

        PublishedCorrectionBannerTests.AssertTheBannerFollowsTheHeadline(corrected);
        Assert.Null(plain.QuerySelector(".correction-banner"));
        Assert.Null(corrected.QuerySelector(".update-line"));
        Assert.Null(plain.QuerySelector(".update-line"));
        PublishedUpdateLineTests.AssertTheLineIsInTheRecordHeader(updated);
    }
}
