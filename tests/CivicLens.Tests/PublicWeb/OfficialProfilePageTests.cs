using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Application.Analysis;
using CivicLens.PublicWeb.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class OfficialProfilePageTests
{
    private const string Root = "https://civic-lens.test/";
    private static readonly string VoteId = new('a', 64);
    private static readonly string BreakId = new('b', 64);
    private static readonly string MissedId = new('c', 64);
    private static readonly string StatementId = new('d', 64);
    private static readonly string BillId = new('e', 64);

    private static OfficialPageData Member() => new(7, "p1", "Ann Able", PublishedOfficeKind.Representative, "House", "MA", 7, "Democrat", null, new(2027, 1, 3), null)
    {
        PublishedOn = new(2026, 11, 1),
        HasLinkedStatements = true,
        MarksTotal = 2,
        Attendance = [new(PublishedChamber.House, 119, 5, 675, new(2025, 1, 3), new(2026, 9, 16))],
        PartyLines = [new(PublishedChamber.House, 119, "Democrat", 248, 1, 249, new(2025, 1, 3), new(2026, 9, 16))],
        BillsCongress = 119,
        IssuesCongress = 119,
        SponsoredBills = 43,
        RecordBills = 44,
        Issues = [new("tax", "Federal taxes", 8)],
        Marks = [new(BreakId, new(2026, 9, 16), PublishedChamber.House, 309, false, "Democrat"),
                 new(MissedId, new(2026, 9, 15), PublishedChamber.House, 305, true, null)],
        Votes = [new(VoteId, new(2026, 9, 16), 119, 2, 314, "House passes S 2403", "AI vote label", "Vote to pass the bill", "Passed", [new("p1", "Yes (Aye)", "Yea")])],
        Statements = [new(StatementId, new(2026, 9, 12), "A statement about H.R. 5334", "AI statement label", VoteId, "House agrees to Senate changes", "AI linked vote label",
            new(2026, 9, 16), PublishedChamber.House, 308, "Nay")],
        Bills = [new(BillId, "hr", null, "H.R. 40 would study reparations.", new(2025, 1, 3),
            "Referred to the House Committee on the Judiciary.", new(2025, 1, 3))]
    };

    private static OfficialPageData President() => new(7, "p4", "Eve East", PublishedOfficeKind.Executive, "President", null, null, "Republican", null, null, null)
    {
        Executive = new(3, new(2025, 1, 20), new(2026, 11, 3),
        [
            new(PublishedExecutiveActionKind.ExecutiveOrder, 2, [new(new string('1', 64), "Housing Costs", new(2026, 10, 22)), new(new string('2', 64), "Women's Health", new(2026, 10, 19))]),
            new(PublishedExecutiveActionKind.Proclamation, 1, [new(new string('3', 64), "Seafood Month", new(2026, 10, 22))])
        ])
    };

    [Fact]
    public async Task TheHeaderNamesTheOfficialAndEachFigureNamesWhatItCountsAndItsCongress()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var figures = page.QuerySelectorAll(".official-figs li").Select(item => item.TextContent).ToArray();

        Assert.Equal("Ann Able", page.QuerySelector("h1")!.TextContent);
        Assert.Contains("Democratic representative for Massachusetts, district 7.", page.QuerySelector(".official-head .official-lede")!.TextContent);
        Assert.Equal(3, figures.Length);
        Assert.Contains("670roll calls voted on, of 675 House roll calls on this site in the 119th Congress.", figures[0]);
        Assert.StartsWith("248of 249 split votes", figures[1]);
        Assert.Contains("cast with most House Democrats, in the 119th Congress.", figures[1]);
        Assert.Contains("43bills and resolutions sponsored in the 119th Congress", figures[2]);
    }

    [Fact]
    public async Task AnAbsentBillCountShowsTheUnavailableCaptionInTheFiguresAndNeverAZero()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member() with { BillsCongress = null, SponsoredBills = 0, BillCountsAbsent = true });

        var figures = page.QuerySelectorAll(".official-figs li").Select(item => item.TextContent).ToArray();
        Assert.Equal(3, figures.Length);
        Assert.Equal(OfficialPageText.BillCountsUnavailable, figures[2]);
        Assert.DoesNotContain("bills and resolutions sponsored", page.QuerySelector(".official-figs")!.TextContent);
        Assert.DoesNotContain("0bills", page.QuerySelector(".official-figs")!.TextContent);
    }

    [Fact]
    public async Task AFigureShowsNoUnavailableCaptionWhenTheBillCountIsStored()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());

        Assert.DoesNotContain(OfficialPageText.BillCountsUnavailable, page.DocumentElement.TextContent);
    }

    [Fact]
    public async Task OneScopedPartyFigureAppearsAndNoSecondPartyCountDuplicatesIt()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());

        Assert.Single(page.QuerySelectorAll(".official-figs li"), item => item.TextContent.Contains("split votes"));
        Assert.DoesNotContain("against most", page.QuerySelector(".official-figs")!.TextContent);
    }

    [Fact]
    public async Task TheStripHoldsOnlyLinksToPartyBreaksAndMissedVotes()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var marks = page.QuerySelectorAll(".official-strip a");

        Assert.Equal([Root + "records/" + BreakId + ".html", Root + "records/" + MissedId + ".html"], marks.Select(mark => mark.GetAttribute("href")!).ToArray());
        Assert.True(marks[0].ClassList.Contains("against"));
        Assert.True(marks[1].ClassList.Contains("missed"));
        Assert.Contains("Voted against most Democrats", marks[0].TextContent);
        Assert.DoesNotContain(page.QuerySelectorAll(".official-strip a"), mark => mark.GetAttribute("href")!.Contains(VoteId));
        var legend = page.QuerySelector(".official-legend")!.TextContent;
        Assert.Contains("1 vote against most House Democrats", legend);
        Assert.Contains("5 missed House roll calls", legend);
    }

    [Fact]
    public async Task AMemberWhoSwitchedPartiesGetsOneLegendLineForEachPartyTheMarksBrokeFrom()
    {
        var data = Member() with
        {
            PartyLines =
            [
                new(PublishedChamber.House, 119, "Democrat", 100, 2, 102, new(2025, 1, 3), new(2025, 6, 1)),
                new(PublishedChamber.House, 119, "Republican", 50, 1, 51, new(2025, 6, 2), new(2026, 9, 16))
            ]
        };
        var page = await RenderAsync<OfficialProfilePage>(data);

        var legend = page.QuerySelectorAll(".official-legend li").Select(item => item.TextContent).ToArray();

        Assert.Contains(legend, line => line.Contains("2 votes against most House Democrats"));
        Assert.Contains(legend, line => line.Contains("1 vote against most House Republicans"));
    }

    [Fact]
    public async Task JumpLinksPointAtTheSectionsThatExistAndEveryTargetExists()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var targets = page.QuerySelectorAll(".official-jump a").Select(link => link.GetAttribute("href")!).ToArray();

        Assert.Equal(["#votes", "#statements", "#bills", "#issues"], targets);
        Assert.All(targets, target => Assert.NotNull(page.QuerySelector(target)));
        Assert.Null(page.QuerySelector(".official-jump p"));
    }

    [Fact]
    public async Task EmptySectionsAreHiddenAndTheLinkedStatementMessageSitsInTheJumpRow()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member() with { Statements = [], HasLinkedStatements = false, Bills = [], Issues = [] });

        Assert.Null(page.QuerySelector("#statements"));
        Assert.Null(page.QuerySelector("#bills"));
        Assert.Null(page.QuerySelector("#issues"));
        Assert.Equal(["#votes"], page.QuerySelectorAll(".official-jump a").Select(link => link.GetAttribute("href")!).ToArray());
        Assert.Equal(OfficialPageText.NoLinks, page.QuerySelector(".official-jump p")!.TextContent);
    }

    [Fact]
    public async Task LinkedStatementsShowTheRelatedVoteWithTheCastAndKeepTheLinkMeaning()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var statements = page.QuerySelector("#statements")!;

        Assert.Equal("What they said and how they voted", statements.QuerySelector("h2")!.TextContent);
        Assert.Contains("Said on 12 Sep 2026", statements.TextContent);
        Assert.Contains("House roll call 308, 16 Sep 2026: House agrees to Senate changes", statements.QuerySelector(".official-related a")!.TextContent);
        Assert.Contains("No (Nay)", statements.QuerySelector(".shared-vote-cast-value")!.TextContent);
        Assert.Equal("nay", statements.QuerySelector(".shared-vote-cast-value")!.GetAttribute("data-side"));
        Assert.Contains(OfficialPageText.LinkMeaning, statements.TextContent);
        Assert.Equal(Root + "method.html", statements.QuerySelector("p.muted a")!.GetAttribute("href"));
    }

    [Theory]
    [InlineData(PublishedApprover.Code, "Code checked this link")]
    [InlineData(PublishedApprover.Person, "A reviewer checked this link")]
    [InlineData(null, null)]
    public async Task AStatementRowShowsTheLabelOfWhoCheckedItsLinkAndTheLinkMeaningNoLongerClaimsAReviewer(PublishedApprover? approver, string? label)
    {
        var data = Member();
        var page = await RenderAsync<OfficialProfilePage>(data with { Statements = [data.Statements[0] with { LinkApprover = approver }] });
        var statements = page.QuerySelector("#statements")!;

        Assert.Equal(label, statements.QuerySelector(".link-checker")?.TextContent);
        Assert.Equal("A link does not mean the vote or bill matches the statement.", OfficialPageText.LinkMeaning);
        Assert.DoesNotContain("A reviewer approved each link", statements.TextContent);
    }

    [Fact]
    public async Task BillRowsShowTheIntroducedDateTheOfficialTitleWithoutABillLineAndAStatusBadge()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var row = page.QuerySelector("#bills .official-rows li")!;

        Assert.Equal("H.R. 40 would study reparations.", row.QuerySelector("a.official-row-title")!.TextContent);
        Assert.Null(row.QuerySelector(".bill-line"));
        Assert.Null(page.QuerySelector("#bills > p.muted"));
        Assert.Contains("Introduced 3 Jan 2025", row.QuerySelector(".official-meta")!.TextContent);
        Assert.Equal(BillStatusMeaning.Label("Referred to the House Committee on the Judiciary.", "hr"), row.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Equal("In committee", row.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Contains("See all 44 bills", page.QuerySelector("#bills .official-more")!.TextContent);
    }

    [Fact]
    public async Task ABillLineShowsBesideTheOfficialTitleWithTheSharedNoteOnceAndAnUnknownActionShowsNoBadge()
    {
        var bill = Member().Bills[0] with { BillLine = "Studies reparations.", LatestAction = "Something odd happened" };
        var page = await RenderAsync<OfficialProfilePage>(Member() with { Bills = [bill, bill with { RecordId = new string('f', 64), BillLine = "Pays landlords." }] });
        var row = page.QuerySelector("#bills .official-rows li")!;

        Assert.Equal("H.R. 40 would study reparations.", row.QuerySelector("a.official-row-title")!.TextContent);
        Assert.Equal("Studies reparations.", row.QuerySelector(".bill-line")!.TextContent);
        Assert.Equal(2, page.QuerySelectorAll("#bills .bill-line").Length);
        Assert.Equal(PublishedWording.BillLineSource, Assert.Single(page.QuerySelectorAll("#bills > p.muted")).TextContent);
        Assert.Null(row.QuerySelector(".badge"));
        Assert.Contains("Latest official action 3 Jan 2025: Something odd happened.", row.QuerySelector(".official-meta")!.TextContent);
    }

    [Fact]
    public async Task SeeAllLinksLeadToTheTabOfTheirKindOnTheFullRecordPageOfTheOfficial()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());

        Assert.Equal([Root + "officials/p1/record.html?kind=votes", Root + "officials/p1/record.html?kind=statements", Root + "officials/p1/record.html?kind=bills"],
            page.QuerySelectorAll(".official-more a").Select(link => link.GetAttribute("href")!).ToArray());
    }

    [Fact]
    public async Task ThePageUsesTheSharedTrailTheOfficialsSectionAndNoInlineStyles()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var trail = page.QuerySelector("nav[aria-label=Breadcrumb]")!;

        Assert.Equal(["Home", "Officials"], trail.QuerySelectorAll("a").Select(link => link.TextContent).ToArray());
        Assert.Equal("Ann Able", trail.QuerySelector("[aria-current=page]")!.TextContent);
        Assert.Equal("page", page.QuerySelectorAll(".public-nav a").Single(link => link.TextContent == "Officials").GetAttribute("aria-current"));
        Assert.Empty(page.QuerySelectorAll("[style]"));
    }

    [Fact]
    public async Task AnOfficialWithNoRecordsShowsOneEmptyMessageAndNoSectionOrFigure()
    {
        var empty = new OfficialPageData(7, "p9", "Fay Fox", null, null, null, null, null, null, null, null);
        var page = await RenderAsync<OfficialProfilePage>(empty);

        Assert.Contains(OfficialPageText.NothingToShow, page.QuerySelector(".official-page")!.TextContent);
        Assert.Empty(page.QuerySelectorAll(".official-section"));
        Assert.Null(page.QuerySelector(".official-figs"));
    }

    [Fact]
    public async Task ThePresidentPageCountsEachTypeLinksItsSectionAndShowsNoSpeechesSection()
    {
        var page = await RenderAsync<PresidentPage>(President());
        var counts = page.QuerySelectorAll(".official-counts a");

        Assert.Equal("President Eve East", page.QuerySelector("h1")!.TextContent);
        Assert.Contains("Civic Lens holds 3 executive actions that the Federal Register lists for this President, signed from 20 Jan 2025 to 3 Nov 2026.", page.QuerySelector(".official-lede")!.TextContent);
        Assert.Equal(["2executive orders signed by Eve East", "1proclamation signed by Eve East"], counts.Select(link => link.TextContent).ToArray());
        Assert.All(counts, link => Assert.NotNull(page.QuerySelector(link.GetAttribute("href")!)));
        Assert.Equal(["Executive orders (2)", "Proclamations (1)"], page.QuerySelectorAll(".official-exec h2").Select(heading => heading.TextContent).ToArray());
        Assert.DoesNotContain("What the President said", page.QuerySelector(".official-page")!.TextContent);
        Assert.DoesNotContain("said", page.QuerySelector(".official-page")!.TextContent.Replace("Women's Health", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFailedMembersLookupHidesTheCompareSectionInsteadOfShowingAnErrorOnEveryOfficialPage()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.PublicWeb", "wwwroot", "js", "public.js"));
        var start = script.IndexOf("const initOfficialMembers", StringComparison.Ordinal);
        var official = script[start..script.IndexOf("\n    };", start, StringComparison.Ordinal)];

        Assert.Contains("hideOnFailure: true,", official, StringComparison.Ordinal);
        Assert.Contains("if (hideOnFailure) { slot.replaceChildren(); onClear?.(); return; }", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePresidentPageAndTheActionPageGiveEachKindTheSameMeaning()
    {
        var kinds = Enum.GetValues<PublishedExecutiveActionKind>();
        var president = await RenderAsync<PresidentPage>(President() with
        {
            Executive = new(kinds.Length, new(2025, 1, 20), new(2026, 11, 3),
                [.. kinds.Select((kind, index) => new OfficialExecutiveType(kind, 1, [new(new string((char)('1' + index), 64), "Title", new(2026, 10, 22))]))])
        });

        foreach (var kind in kinds)
        {
            var action = await RenderAsync<ExecutiveActionDetailPage>(new ExecutiveActionPageData(1, new string('9', 64), 1, "Title", kind, "2026-1",
                new(2026, 10, 20), new(2026, 10, 22), "91 FR 1", "https://www.federalregister.gov/d/2026-1", "p4", "Eve East", [], []));
            var explainer = (IElement)action.QuerySelector("section.executive-explain p")!.Clone(true);
            explainer.QuerySelector(".term-popover")!.Remove();

            Assert.Equal(explainer.TextContent, president.QuerySelector($"#{OfficialPageText.ExecutiveAnchor(kind)} .official-lede")!.TextContent);
        }
    }

    [Fact]
    public async Task PresidentRowsShowTheTypeTheSignedDateAndLinkToTheRecord()
    {
        var page = await RenderAsync<PresidentPage>(President());
        var row = page.QuerySelector(".official-exec .official-rows li")!;

        Assert.Equal(Root + "records/" + new string('1', 64) + ".html", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal(["Executive order", "Signed 22 Oct 2026"], row.QuerySelectorAll(".official-meta > span").Select(part => part.TextContent).ToArray());
    }

    [Fact]
    public async Task EveryPresidentSeeAllLinkLeadsToTheRecordPageOfThePresidentAndNeverToAnAllPresidentSearch()
    {
        var page = await RenderAsync<PresidentPage>(President());
        var links = page.QuerySelectorAll("a.btn, .official-more a");

        Assert.Single(links);
        Assert.All(links, link => Assert.Equal(Root + "officials/p4/record.html?kind=executive-actions", link.GetAttribute("href")));
    }

    [Fact]
    public async Task PresidentTypeLinkAppearsOnlyWhenMoreRecordsExistThanShown()
    {
        var data = President();
        var executive = data.Executive!;
        var page = await RenderAsync<PresidentPage>(data with
        {
            Executive = executive with { Total = 6, Types = [executive.Types[0] with { Count = 5 }, executive.Types[1]] }
        });

        var link = Assert.Single(page.QuerySelectorAll(".official-exec .official-more a"));
        Assert.Equal("See all 5 executive orders", link.TextContent);
        Assert.Equal(Root + "officials/p4/record.html?kind=executive-actions", link.GetAttribute("href"));
        Assert.NotNull(page.QuerySelector("a.btn-primary"));
    }

    [Fact]
    public async Task AStatementLabelComesOnlyFromTheViewButAModelVoteKeepsItsCanonicalLabelWhenItsStoredLabelIsMissing()
    {
        var data = Member();
        var page = await RenderAsync<OfficialProfilePage>(data with
        {
            Statements = [data.Statements[0] with
            {
                Label = null, VoteLabel = null,
                TextOrigin = PublishedTextOrigin.Model, VoteTextOrigin = PublishedTextOrigin.Model
            }]
        });

        var labels = page.QuerySelectorAll("#statements .official-source-label[data-origin=model]");
        Assert.Equal([PublishedRecordLabels.ModelWording], labels.Select(label => label.TextContent).ToArray());
    }

    [Fact]
    public async Task CodeWordingIsVisibleWithoutBeingMarkedAsModelText()
    {
        var data = Member();
        var page = await RenderAsync<OfficialProfilePage>(data with
        {
            Statements = [data.Statements[0] with
            {
                Label = null, TextOrigin = PublishedTextOrigin.Person,
                VoteLabel = PublishedRecordLabels.CodeWording, VoteTextOrigin = PublishedTextOrigin.Code
            }]
        });

        Assert.Empty(page.QuerySelectorAll("#statements .official-source-label[data-origin=model]"));
        Assert.Equal(PublishedRecordLabels.CodeWording, Assert.Single(page.QuerySelectorAll("#statements .official-source-label")).TextContent);
    }

    [Fact]
    public async Task EmptyOfficialPageShowsOneEmptyMessageWithoutAnotherNoLinksMessage()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member() with
        {
            Votes = [],
            Marks = [],
            Statements = [],
            Bills = [],
            Issues = [],
            HasLinkedStatements = false
        });

        Assert.Null(page.QuerySelector(".official-jump"));
        Assert.Contains(OfficialPageText.NothingToShow, page.DocumentElement.TextContent);
        Assert.DoesNotContain(OfficialPageText.NoLinks, page.DocumentElement.TextContent);
    }

    [Fact]
    public async Task FixedPageCopyReadsAtOrBelowTheSharedGradeLimit()
    {
        string[] passages =
        [
            OfficialPageText.LinkMeaning, OfficialPageText.NoLinks, OfficialPageText.BillCountsUnavailable, OfficialPageText.ExecutiveBox,
            .. Enum.GetValues<PublishedExecutiveActionKind>().Select(ExecutiveActionWords.ExplainerText)
        ];

        foreach (var passage in passages)
            Assert.True(ReadingGrade.RoundedPassage([passage]) <= AiTextRules.MaximumGrade, $"\"{passage}\" reads above the limit.");
    }

    [Fact]
    public async Task EveryAiLabelOfAVoteAStatementAndItsLinkedVoteIsShown()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());

        Assert.Contains("AI vote label", page.QuerySelector("#votes .shared-vote-label")!.TextContent);
        var statement = page.QuerySelector("#statements")!.TextContent;
        Assert.Contains("AI statement label", statement);
        Assert.Contains("AI linked vote label", statement);
    }

    [Fact]
    public async Task AVoteRowKeepsItsCongressSessionKindWordsAndOnlyTheCastOfTheOfficialWithoutAName()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var row = page.QuerySelector("#votes .shared-vote-row")!;

        Assert.Contains("119th Congress, session 2", row.QuerySelector(".shared-vote-meta")!.TextContent);
        Assert.Contains("Vote to pass the bill", row.QuerySelector(".shared-vote-meta")!.TextContent);
        Assert.Equal("Yes (Aye)", row.QuerySelector(".shared-vote-cast-value")!.TextContent.Trim());
        Assert.Null(row.QuerySelector(".shared-vote-member"));
        Assert.Null(page.QuerySelector("#votes .shared-vote-caption"));
        Assert.Null(page.QuerySelector(".official-cast"));
    }

    [Fact]
    public async Task AFormerMemberNeverReadsCurrentTermEndsAndACurrentMemberDoes()
    {
        var current = await RenderAsync<OfficialProfilePage>(Member());
        var former = await RenderAsync<OfficialProfilePage>(Member() with { TermEndsOn = new(2026, 1, 3) });

        Assert.Contains("Current term ends 3 Jan 2027.", current.QuerySelector(".official-head .official-lede")!.TextContent);
        var lede = former.QuerySelector(".official-head .official-lede")!.TextContent;
        Assert.StartsWith("Former Democratic representative for Massachusetts, district 7.", lede);
        Assert.DoesNotContain("Current term ends", lede);
    }

    [Fact]
    public void NoTermEndShowsWhenTheReleaseDateIsUnknown()
    {
        var lede = OfficialPageText.Lede(Member() with { PublishedOn = null });

        Assert.DoesNotContain("Current term ends", lede);
        Assert.StartsWith("Democratic representative for Massachusetts, district 7.", lede);
    }

    [Fact]
    public async Task TheStripSquaresNameOnlyTheFactsOfTheTitleAndNoVoteHeadline()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());

        Assert.All(page.QuerySelectorAll(".official-strip a"), mark =>
        {
            Assert.Equal(mark.GetAttribute("title"), mark.QuerySelector(".visually-hidden")!.TextContent);
            Assert.DoesNotContain("House passes", mark.TextContent);
        });
    }

    [Fact]
    public void TheLedeRulesHaveOneOwnerInOfficeLede()
    {
        Assert.Equal("Democratic representative for Washington, district 9", OfficeLede.Of(false, "Democrat", "representative", OfficeLede.LedePlace("WA", 9), null));
        Assert.Equal("Former Independent senator for Washington, caucuses with Democrats", OfficeLede.Of(true, "Independent", "senator", OfficeLede.LedePlace("WA", null), "Democrat"));
        Assert.Equal(" for WA, district 9", OfficeLede.Place("WA", 9));
        Assert.True(OfficeLede.HasEnded(new(2026, 1, 3), new(2026, 1, 4)));
        Assert.False(OfficeLede.HasEnded(new(2026, 1, 3), new(2026, 1, 3)));
    }

    [Fact]
    public async Task TheStripSaysHowManyMarksItShowsOfTheTotalAndLinksTheFullRecordOnlyWhenItIsCut()
    {
        var marks = Enumerable.Range(0, 12).Select(day => new OfficialMarkData(new string((char)('a' + day), 64), new(2026, 9, 1 + day), PublishedChamber.House, 100 + day, false, "Democrat")).ToArray();
        var cut = await RenderAsync<OfficialProfilePage>(Member() with { Marks = marks, MarksTotal = 40 });
        var whole = await RenderAsync<OfficialProfilePage>(Member() with { Marks = marks, MarksTotal = 12 });

        var note = cut.QuerySelector("#votes p.muted")!;
        Assert.StartsWith("The 12 newest of 40.", note.TextContent);
        Assert.Equal(Root + "officials/p1/record.html?kind=votes", note.QuerySelector("a")!.GetAttribute("href"));
        Assert.DoesNotContain("newest of", whole.QuerySelector("#votes")!.TextContent);
    }

    [Fact]
    public async Task TheNoLinksMessageShowsOnlyWhenNoStatementLinksToAVoteOrABill()
    {
        var onlyBillLinks = await RenderAsync<OfficialProfilePage>(Member() with { Statements = [], HasLinkedStatements = true });
        var none = await RenderAsync<OfficialProfilePage>(Member() with { Statements = [], HasLinkedStatements = false });

        Assert.Null(onlyBillLinks.QuerySelector(".official-jump p"));
        Assert.Equal(OfficialPageText.NoLinks, none.QuerySelector(".official-jump p")!.TextContent);
    }

    [Fact]
    public async Task TheStripNeverNamesACongressZeroWhenNoFigureGivesTheCongress()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member() with { Attendance = [], PartyLines = [], BillsCongress = null, Issues = [] });

        Assert.NotEmpty(page.QuerySelectorAll(".official-strip a"));
        Assert.DoesNotContain("0th", page.DocumentElement.TextContent);
        Assert.Null(page.QuerySelector("#votes .official-lede"));
    }

    [Fact]
    public async Task AnIssueSectionWithoutItsCongressIsHiddenAndAPageWithNoSectionHasNoJumpList()
    {
        var noCongress = await RenderAsync<OfficialProfilePage>(Member() with { IssuesCongress = null });
        var nothing = await RenderAsync<OfficialProfilePage>(Member() with { Marks = [], Votes = [], Statements = [], Bills = [], Issues = [], HasLinkedStatements = true });

        Assert.Null(noCongress.QuerySelector("#issues"));
        Assert.DoesNotContain("0th", noCongress.DocumentElement.TextContent);
        Assert.Empty(nothing.QuerySelectorAll(".official-jump ul"));
        Assert.Null(nothing.QuerySelector(".official-jump"));
    }

    [Fact]
    public void ABreakMarkWithoutAPartyIsAnInvalidMarkAndNoFallbackWordHidesIt()
    {
        var invalid = new OfficialMarkData(BreakId, new(2026, 9, 16), PublishedChamber.House, 1, false, null);

        Assert.Throws<InvalidDataException>(() => OfficialPageText.MarkName(invalid));
    }

    [Fact]
    public void TheStripHitAreasOfNeighborsDoNotOverlap()
    {
        var css = WebStyleSheets.Read("public.css");
        var gap = Px(css, @"\.official-strip \{[^}]*?gap: var\(--s(\d)\)");
        var reach = Px(css, @"\.official-strip a::after \{[^}]*?inset: -(\d+)px");

        Assert.True(reach * 2 <= gap, $"Each hit area reaches {reach}px, and the gap between squares is {gap}px.");
    }

    private static int Px(string css, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(css, pattern);
        Assert.True(match.Success, pattern);
        var value = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return pattern.Contains("--s", StringComparison.Ordinal) ? new[] { 0, 4, 8, 12, 16, 24, 32, 48 }[value] : value;
    }

    private static async Task<IDocument> RenderAsync<TComponent>(object data) where TComponent : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Data"] = data,
                ["RootPath"] = Root
            }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
