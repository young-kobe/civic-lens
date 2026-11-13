using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Application.Actions;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class PublicPageReadingGradeTests
{
    private const string Root = "https://civic-lens.test/";
    private const double PageTextCeiling = 14;
    private const string DifficultSummary = "Notwithstanding the aforementioned administrative requirements, eligible beneficiaries receive compensatory disbursements.";

    [Theory]
    [InlineData("home")]
    [InlineData("compare")]
    public async Task HomeAndCompareIntroPassagesReadAtOrBelowThePageTextCeiling(string pageName)
    {
        var page = pageName == "home"
            ? await RenderAsync<HomePage>(new Dictionary<string, object?>
            {
                [nameof(HomePage.Data)] = new HomePageData(7, null, [], null),
                [nameof(HomePage.RootPath)] = Root
            })
            : await RenderAsync<ComparePage>(new Dictionary<string, object?>
            {
                [nameof(ComparePage.RootPath)] = Root
            });

        var intro = Assert.Single(page.QuerySelectorAll(".home-intro"));
        var heading = Assert.Single(intro.QuerySelectorAll("h1"));
        var lead = Assert.Single(intro.QuerySelectorAll(".home-lead"));
        var grade = PassageGrade(heading, lead);

        Assert.True(grade <= PageTextCeiling,
            $"The {pageName} intro reads at grade {ReadingGrade.Format(grade)}.");
    }

    [Fact]
    public async Task ListAndBoardIntroPassagesReadAtOrBelowThePageTextCeiling()
    {
        var officials = await RenderAsync<OfficialsPage>(new Dictionary<string, object?>
        {
            [nameof(OfficialsPage.Data)] = new OfficialsPageData(7, 542, [], OfficialsFilterData.None, null),
            [nameof(OfficialsPage.RootPath)] = Root
        });
        var boards = await RenderAsync<BoardsPage>(new Dictionary<string, object?>
        {
            [nameof(BoardsPage.Data)] = new BoardsPageData(7, [], []),
            [nameof(BoardsPage.RootPath)] = Root
        });

        foreach (var page in new[] { officials, boards })
        {
            var header = Assert.Single(page.QuerySelectorAll(".page-header"));
            var grade = PassageGrade(Assert.Single(header.QuerySelectorAll("h1")), Assert.Single(header.QuerySelectorAll(".page-header-lede")));
            Assert.True(grade <= PageTextCeiling, $"A list page intro reads at grade {ReadingGrade.Format(grade)}.");
        }
    }

    [Fact]
    public async Task TheBillPageFixedProseReadsAtOrBelowThePageTextCeiling()
    {
        var sponsor = new BillSponsorData("p1", "Pat One", "Democrat", new(2025, 9, 11));
        var data = new BillPageData(7, new string('a', 64), 1, 119, "H.R. 7", "Title", "Headline", PublishedChamber.House, "Summary.", PublishedTextOrigin.Code, null, new(2025, 9, 11))
        {
            Sponsors = [sponsor],
            Cosponsors = new(PublishedChamber.House, [sponsor], [sponsor]),
            Status = "Passed the House. Now in the Senate.",
            StatusLabel = "Passed the House",
            CongressGovUrl = "https://www.congress.gov/bill/119th-congress/house-bill/7",
            Votes = [new(new string('b', 64), new(2026, 9, 16), 119, 2, 410, "Vote headline", null, "Vote to pass or agree to", "Passed", [])],
            StatusReadAtUtc = new(2026, 11, 5, 9, 57, 0, TimeSpan.Zero)
        };
        var page = await RenderAsync<BillDetailPage>(new Dictionary<string, object?>
        {
            [nameof(BillDetailPage.Data)] = data,
            [nameof(BillDetailPage.RootPath)] = Root
        });

        var grade = PassageGrade(page.QuerySelector(".bill-lede")!, page.QuerySelector(".bill-legend")!);
        var backers = PassageGrade(page.QuerySelector("#bill-backers-title")!, page.QuerySelector("#bill-backers-title")!.ParentElement!.QuerySelector(".bill-lede")!);

        var split = PassageGrade(page.QuerySelector("#bill-backers-title")!.ParentElement!.QuerySelector(".bill-lede")!, page.QuerySelector(".party-bar figcaption")!);
        var votes = PassageGrade(page.QuerySelector("#bill-votes-title")!, page.QuerySelector("#bill-votes-title")!.ParentElement!.QuerySelector(".bill-lede")!);
        var worst = new[] { grade, backers, split, votes }.Max();

        Assert.True(worst <= PageTextCeiling, $"The bill page prose reads at grade {ReadingGrade.Format(worst)}.");
    }

    [Fact]
    public async Task TheVoteKindIndexTextReadsAtOrBelowThePageTextCeiling()
    {
        var page = await RenderAsync<VoteKindsPage>(new Dictionary<string, object?> { [nameof(VoteKindsPage.RootPath)] = Root });

        var meanings = page.QuerySelectorAll("tbody tr td:last-child");
        Assert.Equal(VoteMeaning.Kinds.Count + 1, meanings.Length);
        var texts = meanings.Append(Assert.Single(page.QuerySelectorAll(".page-header-lede")));
        foreach (var text in texts)
        {
            var grade = ReadingGrade.Rounded(NormalizeText(text));
            Assert.True(grade <= PageTextCeiling, $"\"{text.TextContent}\" reads at grade {ReadingGrade.Format(grade)}.");
        }
    }

    [Fact]
    public async Task FeaturedModelHeadlineAndSummaryReadAsOnePassageAtOrBelowTheSharedGradeLimit()
    {
        // Civic Lens model text captured on 2026-11-10, release 1050.
        // https://civic-lens.info/records/d5bb27866c52e5e9e1b4f8b5e3801dc6ca8b129d7e584746f92dad470e25b337.html
        var featured = new HomeFeaturedVoteData("d5bb27866c52e5e9e1b4f8b5e3801dc6ca8b129d7e584746f92dad470e25b337", new(2026, 9, 28),
            PublishedChamber.Senate,
            "Senate passes the Protect College Sports Act of 2026, a bill on student athlete name, image, and likeness rights and fair competition in college athletics",
            null,
            "The Senate voted on passage of the Protect College Sports Act of 2026. The question was whether the bill should pass, and the Senate answered yes, so the bill passed. The bill's stated purpose is to protect the name, image, and likeness rights of student athletes, to provide protections for them, and to promote fair competition among intercollegiate athletics, along with other purposes. The bill falls under the policy area of sports and recreation.",
            PublishedTextOrigin.Model, "Passed", 77, 22, null, null);
        var page = await RenderAsync<HomePage>(new Dictionary<string, object?>
        {
            [nameof(HomePage.Data)] = new HomePageData(1050, featured, [], null),
            [nameof(HomePage.RootPath)] = Root
        });

        var grade = FeaturedModelGrade(page);
        Assert.True(grade <= ReadingGrade.MaximumGrade,
            $"The featured model passage reads at grade {ReadingGrade.Format(grade)}.");
    }

    [Fact]
    public async Task EasyCopyOutsideTheFeaturedModelPassageCannotLowerItsGrade()
    {
        var featured = new HomeFeaturedVoteData(new string('a', 64), new(2026, 3, 3),
            PublishedChamber.House, "A clear headline", null, DifficultSummary,
            PublishedTextOrigin.Model, "Passed", 216, 214, null, null);
        var page = await RenderAsync<HomePage>(new Dictionary<string, object?>
        {
            [nameof(HomePage.Data)] = new HomePageData(7, featured, [], null),
            [nameof(HomePage.RootPath)] = Root
        });
        var isolatedGrade = FeaturedModelGrade(page);
        var modelSection = Assert.Single(page.QuerySelectorAll(".home-featured"));
        var easyCopy = page.CreateElement("p");
        easyCopy.TextContent = string.Join(' ', Enumerable.Repeat("The cat sat. The dog ran.", 12));
        modelSection.AppendChild(easyCopy);
        var gradeAfterUnrelatedCopy = FeaturedModelGrade(page);

        Assert.True(isolatedGrade > ReadingGrade.MaximumGrade);
        Assert.Equal(isolatedGrade, gradeAfterUnrelatedCopy);
        Assert.True(ReadingGrade.Rounded(NormalizeText(modelSection)) <= ReadingGrade.MaximumGrade,
            "The whole section can hide a difficult model passage behind unrelated easy text.");
    }

    private static readonly string[] BlockTags = ["P", "LI", "H1", "H2", "H3", "H4", "H5", "H6", "FIGCAPTION", "CAPTION", "SUMMARY", "DD", "DT", "LABEL", "LEGEND", "TD", "TH"];
    private static readonly string[] HeadingTags = ["H1", "H2", "H3", "H4", "H5", "H6"];
    /// <summary>Record data that the gate does not grade. A record table is a paged table (data-paged) or the table in the timeline panel (aria-labelledby timeline-title).
    /// Every other table holds fixed text, such as the privacy rows on the About page, so its cells are graded.</summary>
    private const string RecordDataSelector = "script, style, pre, blockquote, .ai-summary > p:not([class]), .doc-text, option, table[data-paged], [aria-labelledby=timeline-title] table";
    private static readonly string[] ContainerTags = ["DIV", "SECTION", "ARTICLE", "ASIDE", "MAIN", "HEADER", "FOOTER", "NAV", "FORM", "FIELDSET", "DETAILS", "FIGURE", "UL", "OL", "DL", "TABLE", "THEAD", "TBODY", "TR"];
    private const int MinimumPassageWords = 6;

    /// <summary>The fixed text of a page: each block of at least <see cref="MinimumPassageWords"/> words that holds no record data. The first block under a heading
    /// reads together with that heading, as an intro does. A shorter block is a label, not a passage. Text that sits loose in a container, such as an empty state with a link, is a block too.</summary>
    private static List<string[]> FixedPassages(IDocument page, string[] recordData)
    {
        foreach (var data in page.QuerySelectorAll(RecordDataSelector).ToArray()) data.Remove();
        WrapLooseText(page);
        var passages = new List<string[]>();
        string? heading = null;
        foreach (var block in page.QuerySelectorAll(string.Join(',', BlockTags)).Where(block => !block.QuerySelectorAll(string.Join(',', BlockTags)).Any()))
        {
            var text = NormalizeText(block);
            if (text.Length == 0) continue;
            if (HeadingTags.Contains(block.TagName))
            {
                heading = recordData.Any(text.Contains) ? null : text;
                continue;
            }
            if (recordData.Any(text.Contains) || text.Split(' ').Length < MinimumPassageWords) continue;
            passages.Add(heading is null ? [text] : [heading, text]);
            heading = null;
        }
        return passages;
    }

    /// <summary>Wraps each run of loose text and inline elements in a container into a paragraph, so the run is graded as one block.</summary>
    private static void WrapLooseText(IDocument page)
    {
        foreach (var container in page.QuerySelectorAll(string.Join(',', ContainerTags)).ToArray())
        {
            var run = new List<INode>();
            foreach (var node in container.ChildNodes.ToArray())
            {
                if (node is IElement element && (BlockTags.Contains(element.TagName) || ContainerTags.Contains(element.TagName)))
                {
                    Wrap(page, container, run);
                    continue;
                }
                run.Add(node);
            }
            Wrap(page, container, run);
        }
    }

    private static void Wrap(IDocument page, IElement container, List<INode> run)
    {
        if (run.Any(node => !string.IsNullOrWhiteSpace(node.TextContent)))
        {
            var paragraph = page.CreateElement("p");
            container.InsertBefore(paragraph, run[0]);
            foreach (var node in run) paragraph.AppendChild(node);
        }
        run.Clear();
    }

    [Fact]
    public async Task EveryFixedPassageOnEveryServerPageReadsAtOrBelowThePageTextCeiling()
    {
        var failures = new Dictionary<string, double>();
        foreach (var (name, render, recordData) in await ServerPagesAsync())
        {
            var passages = FixedPassages(await render(), recordData);
            Assert.True(passages.Count > 0, $"The {name} page has no fixed passage to grade.");
            foreach (var passage in passages)
            {
                var grade = ReadingGrade.RoundedPassage(passage);
                if (grade > PageTextCeiling) failures.TryAdd(string.Join(" | ", passage), grade);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Select(pair => $"\"{pair.Key}\" reads at grade {ReadingGrade.Format(pair.Value)}.")));
    }

    [Fact]
    public async Task AHardLongSentenceInFixedPageTextFailsTheGate()
    {
        var page = await RenderAsync<ComparePage>(new Dictionary<string, object?> { [nameof(ComparePage.RootPath)] = Root });
        var extra = page.CreateElement("p");
        extra.TextContent = DifficultSummary;
        page.QuerySelector("main, body")!.AppendChild(extra);

        var worst = FixedPassages(page, []).Max(ReadingGrade.RoundedPassage);

        Assert.True(worst > PageTextCeiling);
    }

    [Fact]
    public async Task FixedTableCellsAndInlineOnlyNoticesAreGraded()
    {
        var about = FixedPassages(await RenderAsync<AboutPage>(new() { [nameof(AboutPage.RootPath)] = Root }), []);
        var report = FixedPassages(await RenderAsync<PublishedReportPage>(new()
        {
            [nameof(PublishedReportPage.PublicApiUrl)] = "https://api.civic-lens.test",
            [nameof(PublishedReportPage.TurnstileSiteKey)] = "key"
        }), []);

        Assert.Contains(about, passage => passage[^1] == "Cloudflare delivers our pages. Like any web host, it sees your IP address and the page you open.");
        Assert.Contains(report, passage => passage[^1].StartsWith("This page needs JavaScript", StringComparison.Ordinal));
    }

    private static async Task<(string Name, Func<Task<IDocument>> Render, string[] RecordData)[]> ServerPagesAsync()
    {
        var sponsor = new BillSponsorData("p1", "Pat One", "Democrat", new(2025, 9, 11));
        var bill = new BillPageData(7, new string('a', 64), 2, 119, "H.R. 5334", "The Example Act of 2026", "H.R. 5334 headline", PublishedChamber.House,
            "The bill would add a tax credit.", PublishedTextOrigin.Model, "An AI summary of this bill.", new(2025, 9, 11))
        {
            Sponsors = [sponsor],
            Cosponsors = new(PublishedChamber.House, [new("p2", "Ann Two", "Democrat", new(2025, 9, 11))], [new("p4", "Cy Four", "Republican", new(2025, 10, 2))]),
            Steps = [new("Introduced in the House", "11 Sep 2025, by Pat One"), new("Became law", "18 Sep 2026, Public Law 119-111")],
            PublicLaw = "119-111",
            BecameLawOn = new(2026, 9, 18),
            Status = "Became law: Public Law 119-111.",
            StatusLabel = "Became law",
            CongressGovUrl = "https://www.congress.gov/bill/119th-congress/house-bill/5334",
            StatusDate = new(2026, 9, 18),
            StatusReadAtUtc = new(2026, 11, 5, 9, 57, 0, TimeSpan.Zero),
            CheckedAtUtc = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero),
            CrsText = "This bill imposes sanctions.",
            CrsVersion = "55",
            SummaryFromCrs = true,
            Issues = [new("foreign-policy", "Foreign policy and war")],
            PolicyArea = "International Affairs",
            BillLine = "This bill imposes sanctions.",
            Votes = [new(new string('b', 64), new(2026, 9, 16), 119, 2, 410, "House agrees to Senate changes", null, "Vote to pass or agree to", "Passed", [])]
        };
        var featured = new HomeFeaturedVoteData(new string('a', 64), new(2026, 3, 3), PublishedChamber.House, "Published headline", null, "Published summary",
            PublishedTextOrigin.Model, "Passed", 216, 214, "Current bill line.", PublishedWording.BillLineSource);
        var recent = Enumerable.Range(0, 4).Select(index => index < 2 ? PublishedChamber.House : PublishedChamber.Senate).Select((chamber, index) => new HomeVoteData(
            new string((char)('a' + index), 64), new(2026, 3, 2), chamber, "Published vote", "AI label", "Passed",
            VotePageView.ChamberBar(chamber, 216, 214, 0, 5, VoteThreshold.None), "Current bill line.")
        { Alignment = PublishedVoteAlignment.Split }).ToArray();
        static BoardPanelData Board(PublishedChamber chamber, string party, string name, int cast, int breaks) =>
            new(chamber, party, 119, null, new DateOnly(2026, 9, 16), true, [new(PublicationProtocol.OfficialPagePath(name), name, null, cast, breaks)]);
        BoardPanelData[] fourBoards =
        [
            Board(PublishedChamber.House, "Democrat", "Henry Cuellar", 254, 160), Board(PublishedChamber.House, "Republican", "James Moylan", 51, 26),
            Board(PublishedChamber.Senate, "Democrat", "John Fetterman", 337, 66), Board(PublishedChamber.Senate, "Republican", "Lisa Murkowski", 341, 58)
        ];
        var home = new HomePageData(7, featured, recent, new(new string('f', 64), "Published brief", PublishedWording.ModelBrief))
        {
            Issues = [new("health", "Health", 119, 3, 12)],
            ExecutiveActions = [new(PublishedExecutiveActionKind.Notice, 4, new string('c', 64), "Newest notice title", new(2026, 5, 31))],
            FeaturedVoteDetail = VoteDetailPageFixture.Vote() with { Members = [MapMember("Ada Able", "Aye", "Democrat", "MA", 7), MapMember("Ben Baker", "Nay", "Republican", "TX", 1)] },
            Boards = fourBoards
        };
        var official = new OfficialPageData(7, "p1", "Ann Able", PublishedOfficeKind.Representative, "House", "MA", 7, "Democrat", null, new(2027, 1, 3), null)
        {
            PublishedOn = new(2026, 11, 1),
            Terms = [new() { Office = "Representative", Kind = PublishedOfficeKind.Representative, State = "MA", District = 7, Party = "Democrat", Start = new(2019, 1, 3) }],
            Website = "https://able.house.gov",
            HasLinkedStatements = true,
            MarksTotal = 2,
            Attendance = [new(PublishedChamber.House, 119, 5, 675, new(2025, 1, 3), new(2026, 9, 16))],
            PartyLines = [new(PublishedChamber.House, 119, "Democrat", 248, 1, 249, new(2025, 1, 3), new(2026, 9, 16))],
            BillsCongress = 119,
            IssuesCongress = 119,
            SponsoredBills = 43,
            Issues = [new("tax", "Federal taxes", 8)],
            Marks = [new(new string('b', 64), new(2026, 9, 16), PublishedChamber.House, 309, false, "Democrat"), new(new string('c', 64), new(2026, 9, 15), PublishedChamber.House, 305, true, null)],
            Votes = [new(new string('a', 64), new(2026, 9, 16), 119, 2, 314, "House passes S 2403", "AI vote label", "Vote to pass the bill", "Passed", [new("p1", "Yes (Aye)", "Yea")])],
            Statements = [new(new string('d', 64), new(2026, 9, 12), "A statement about H.R. 5334", "AI statement label", new string('a', 64), "House agrees to Senate changes", "AI linked vote label",
                new(2026, 9, 16), PublishedChamber.House, 308, "Nay") { LinkApprover = PublishedApprover.Person }],
            Bills = [new(new string('e', 64), "hr", null, "H.R. 40 would study reparations.", new(2025, 1, 3), "Referred to the House Committee on the Judiciary.", new(2025, 1, 3))]
        };
        var president = new OfficialPageData(7, "p4", "Eve East", PublishedOfficeKind.Executive, "President", null, null, "Republican", null, null, null)
        {
            Executive = new(3, new(2025, 1, 20), new(2026, 11, 3),
            [
                new(PublishedExecutiveActionKind.ExecutiveOrder, 2, [new(new string('1', 64), "Housing Costs", new(2026, 10, 22)), new(new string('2', 64), "Women's Health", new(2026, 10, 19))]),
                new(PublishedExecutiveActionKind.Proclamation, 1, [new(new string('3', 64), "Seafood Month", new(2026, 10, 22))])
            ])
        };
        var issue = new IssuePageData(7, "firearms", "Firearms", "Federal firearms law and regulation.", ["Background checks"], ["Crime statements"], 119, 3, 2, 186,
            [new(new string('a', 64), new(2026, 3, 3), PublishedChamber.Senate, "Senate passes a firearms bill", null, "Passed", 52, 48)],
            [new(new string('b', 64), "H.R. 1 would set storage rules.", PublishedRecordLabels.ModelWording, new(2026, 2, 1), "Current bill line.", "Rosa DeLauro")],
            new IssueLineUpData(119, 3, [new("Democrat", 2), new("Republican", 1)], 0));
        var search = new SearchPageData(new("5334", null, null, null, null, null), 1028,
            [new("jimmy-panetta", "Jimmy Panetta", PublishedOfficeKind.Representative, null, "CA", 19, "Democrat")], true,
            new(new string('b', 64), "H.R. 5334, the Sanctioning Act", 119, "Jimmy Panetta", "119-111", new(2026, 9, 18), new(new string('v', 64), PublishedChamber.House), []),
            [new(PublishedRecordKind.Action, new string('a', 64), "H.R. 5334 would impose sanctions", new(2026, 2, 23), null, null, ["Jimmy Panetta"])],
            "next", [new("housing", "Housing")], null);
        var brief = new BriefPageData(7, new string('b', 64), 3, new DateOnly(2026, 11, 2), "The week in Congress", PublishedTextOrigin.Model, "An AI draft from reviewed records.",
            [new PublishedBriefParagraph { Text = "First paragraph.", CitationIds = ["R1"] }],
            [new PublishedBriefCitation { Id = "R1", Kind = PublishedRecordKind.Statement, RecordId = DetailPageFixture.StatementId, RevisionNumber = 2, Headline = "Kelly announces a bill" }],
            9, new PublishedBriefCoverage { Considered = 12, SentToModel = 8, Cited = 1 }, new DateTimeOffset(2026, 11, 6, 13, 5, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 6, 14, 0, 0, TimeSpan.Zero));
        var boards = new BoardsPageData(7, [new(PublishedChamber.House, "Democrat", 119, null, new DateOnly(2026, 9, 16), true,
            [new(PublicationProtocol.OfficialPagePath("cuellar"), "Henry Cuellar", null, 254, 160)])],
            [new(PublishedChamber.House, 119, [new(PublishedVoteKind.Passage, 15, 201)], 196, 254, 0, 450, 0)]);
        var zip = new ZipMemberCardsData(8, 119, "36009", 2,
            [new("Representative for Alabama, district 2", new ZipMemberIdentityData("pat", "Pat Member", "Democratic", new(2029, 1, 3), null, [], [])), new("Senator for Alabama", null)], []);
        var differs = new SharedVoteRowData(new string('d', 64), new(2026, 9, 16), 119, 2, 314, "House members differ", null, "Vote to pass", "Passed",
            [new("pat", "Yes (Yea)", "Yea"), new("sam", "No (Nay)", "Nay")])
        { MembersVotedTheSame = false };
        var zipCompare = new ZipMemberCardsData(8, 119, "36009", 2,
            [new("Representative for Alabama, district 2", new ZipMemberIdentityData("pat", "Pat Member", "Democratic", null, null, [], [])),
                new("Representative for Alabama, district 1", new ZipMemberIdentityData("sam", "Sam Member", "Republican", null, null, [], [])),
                new("Senator for Alabama", null) { Chamber = PublishedChamber.Senate }], [])
        {
            SharedVotes = [new(PublishedChamber.House, [new("pat", "Pat Member"), new("sam", "Sam Member")], [differs])],
            IssueOptions = [new("health", "Health")],
            SelectedIssue = "health"
        };
        var billMembers = new BillMembersData("36009", new string('a', 64), 7,
        [
            new("Pat Member", PublishedChamber.House, BillMemberRelation.Sponsor, null, [new(new(2026, 9, 16), "Yes (Yea)", "Yea")]),
            new("Sam Member", PublishedChamber.House, BillMemberRelation.Cosponsor, new(2025, 10, 2), []),
            new("Sue Senator", PublishedChamber.Senate, BillMemberRelation.None, null, [])
        ], [PublishedChamber.House]);
        var voteList = new VoteListPageData(7, 1250,
            [new(new string('a', 64), "House passes H.R. 5334", "AI vote label", PublishedTextOrigin.Model, PublishedChamber.House, 314, new(2026, 9, 16), PublishedVoteKind.Passage, "H.R. 5334", "Passed", PublishedVoteAlignment.Split, 216, 214)],
            RecordListFilter.Of(null, null), "next", [new("passage", "Vote to pass")], [new("health", "Health")]);
        var billList = new BillListPageData(7, 119, 9000,
            [new(new string('b', 64), "H.R. 5334 would impose sanctions", "AI bill label", PublishedTextOrigin.Model, "hr", "Jimmy Panetta", new(2026, 2, 23), "Became Public Law No: 119-111.", new(2026, 9, 18), BillStatusStage.BecameLaw)],
            RecordListFilter.Of(null, null), "next", [new("law", "Became law")], [new("international-affairs", "International Affairs")], [new("health", "Health")]);
        var statementList = new StatementListPageData(7, 300,
            [new(new string('d', 64), "Kelly announces a bill", "AI statement label", PublishedTextOrigin.Model, new(2026, 9, 12), [new("Mark Kelly", PublishedOfficeKind.Senator, "Senator", "AZ", null, "Democrat")], "kelly.senate.gov", true)],
            RecordListFilter.Of(null, null), "next", [new("health", "Health")]);
        var actionList = new ExecutiveActionListPageData(7, 40,
            [new(new string('3', 64), "Seafood Month", PublishedExecutiveActionKind.Proclamation, new(2026, 10, 22))],
            RecordListFilter.Of(null, null), null, "next", [new("notice", "Notice")]);
        string[] ListRecordData = ["H.R. 5334", "AI vote label", "AI bill label", "Jimmy Panetta", "Public Law", "Kelly announces", "AI statement label", "Mark Kelly", "kelly.senate.gov", "Seafood Month",
            "2026-13-40", "abc", "zzq", "soon"];
        string[] ZipRecordData = ["Pat Member", "Sam Member", "Sue Senator", "House members differ", "Health"];

        string[] VoteRecordData = ["Sanctioning", "H.R. 5334", "A bill line", "voted on a bill", "Ada Able", "Ben Baker", "Cy Cole", "Di Dole", "Ed Eaton", "Dee Cee", "Pat Rico", "Sam Solo", "Ed Ward", "Cal Ray"];
        static VoteMemberData MapMember(string name, string cast, string party, string? state, int? district) =>
            new("o-" + name, name, cast, party, VoteDetailPageFixture.Cite(VoteDetailPageFixture.Lines[4]), state, district);
        static Func<Task<IDocument>> Page<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent => () => RenderAsync<TComponent>(parameters.Where(pair => typeof(TComponent).GetProperty(pair.Key) is not null).ToDictionary());
        static Func<Task<IDocument>> Shell<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent => () => RenderAsync<ServerPageFrame>(new()
        {
            [nameof(ServerPageFrame.Context)] = new ServerPageContext(new(2026, 5, 31), new(2026, 5, 29)),
            [nameof(ServerPageFrame.Page)] = typeof(TComponent),
            [nameof(ServerPageFrame.Parameters)] = parameters.ToDictionary(pair => pair.Key, pair => pair.Value!)
        });
        static Dictionary<string, object?> With(string name, object? data, params (string Name, object? Value)[] more) =>
            new Dictionary<string, object?> { [name] = data, ["RootPath"] = Root }.Concat(more.Select(pair => KeyValuePair.Create(pair.Name, pair.Value))).ToDictionary();

        return
        [
            ("home", Page<HomePage>(With("Data", home, ("BrowserApiUrl", "https://api.civic-lens.test"), ("FeaturedMapHref", "/web/records/" + VoteDetailPageFixture.RecordId + "/map.html"))),
                ["Published", "Newest notice", "Health", "Current bill line", "Henry Cuellar", "James Moylan", "John Fetterman", "Lisa Murkowski", "Ada Able", "Ben Baker"]),
            ("home empty", Page<HomePage>(With("Data", new HomePageData(7, null, [], null))), []),
            ("compare", Page<ComparePage>(With("Data", null)), []),
            ("officials", Page<OfficialsPage>(With("Data", new OfficialsPageData(7, 542, [], OfficialsFilterData.None, null))), []),
            ("officials filtered", Page<OfficialsPage>(With("Data", new OfficialsPageData(7, 542, [], new(null, null, null, "zzq"), null))), []),
            ("boards", Page<BoardsPage>(With("Data", boards)), ["Henry Cuellar"]),
            ("boards empty", Page<BoardsPage>(With("Data", new BoardsPageData(7, [], []))), []),
            ("vote kinds", Page<VoteKindsPage>(With("Data", null)), []),
            ("bill", Page<BillDetailPage>(With("Data", bill)), ["Pat One", "Ann Two", "Cy Four", "Example Act", "headline", "tax credit", "House agrees", "imposes sanctions", "International Affairs"]),
            ("bill bare", Page<BillDetailPage>(With("Data", bill with { Votes = [], Issues = [], PolicyArea = null, Steps = [], Status = null, StatusLabel = null, Sponsors = [], Cosponsors = null, BillLine = null, CrsText = null, SenateVoteGap = true, TextOrigin = PublishedTextOrigin.Code })), ["Example Act", "headline", "tax credit"]),
            ("vote", Page<VoteDetailPage>(With("Data", VoteDetailPageFixture.Vote(), ("PagePath", VoteDetailPageFixture.PagePath), ("Filter", new VoteMemberFilter()))), ["Sanctioning", "H.R. 5334", "A bill line", "voted on a bill", "Ada Able", "Ben Baker", "Cy Cole", "Di Dole", "Ed Eaton"]),
            ("vote house map", Page<VoteDetailPage>(With("Data", VoteDetailPageFixture.Vote() with { Kind = PublishedVoteKind.Suspension, Members = [MapMember("Ada Able", "Aye", "Democrat", "MA", 7), MapMember("Dee Cee", "Present", "Democrat", "DC", 0), MapMember("Pat Rico", "Nay", "Republican", "PR", 0), MapMember("Sam Solo", "Aye", "Republican", null, null)] }, ("PagePath", VoteDetailPageFixture.PagePath), ("Filter", new VoteMemberFilter { Name = "zzq" }))), VoteRecordData),
            ("vote senate map", Page<VoteDetailPage>(With("Data", VoteDetailPageFixture.Vote() with { Chamber = PublishedChamber.Senate, Kind = PublishedVoteKind.VetoOverride, Members = [MapMember("Ed Ward", "Nay", "Democrat", "MA", null), MapMember("Cal Ray", "Not Voting", "Republican", "TX", null)], PartyTallies = null, Bill = null, Issues = [] }, ("PagePath", VoteDetailPageFixture.PagePath), ("Filter", new VoteMemberFilter()))), VoteRecordData),
            ("vote statements", Page<VoteDetailPage>(With("Data", VoteDetailPageFixture.Vote() with
            {
                Bill = VoteDetailPageFixture.Vote().Bill! with { StatusLabel = "Became law", PublicLaw = "119-111", StatusDate = new(2026, 9, 18) },
                Statements = [new(new string('d', 64), new(2026, 9, 12), "Kelly speaks on the bill", "AI statement label", PublishedTextOrigin.Model, PublishedApprover.Person, [new("o1", "Ada Able"), new("o9", "Una Unseen")]),
                    new(new string('e', 64), null, "Schiff speaks on the bill", null, PublishedTextOrigin.Person, null, [new("o2", "Ben Baker")])]
            }, ("PagePath", VoteDetailPageFixture.PagePath), ("Filter", new VoteMemberFilter()))), [.. VoteRecordData, "speaks on the bill", "AI statement label", "Una Unseen"]),
            ("vote map page", Page<VoteMapPage>(With("Data", VoteDetailPageFixture.Vote() with { Members = [MapMember("Ada Able", "Aye", "Democrat", "MA", 7), MapMember("Dee Cee", "Present", "Democrat", "DC", 0)] })), VoteRecordData),
            ("vote map page empty", Page<VoteMapPage>(With("Data", VoteDetailPageFixture.Vote())), VoteRecordData),
            ("official bare", Page<OfficialProfilePage>(With("Data", new OfficialPageData(7, "p9", "Una Unseen", PublishedOfficeKind.Senator, "Senate", "TX", null, null, null, null, null) { BillCountsAbsent = true })), ["Una Unseen"]),
            ("statement bare", Page<StatementDetailPage>(With("Data", DetailPageFixture.Statement(PublishedTextOrigin.Person, dated: false, quotes: 0))), ["Kelly announces", "A model wrote", "Mark Kelly", "Adam Schiff"]),
            ("statement many quotes", Page<StatementDetailPage>(With("Data", DetailPageFixture.Statement(quotes: 2))), ["Kelly announces", "A model wrote", "Mark Kelly", "Adam Schiff"]),
            ("document change dated", Page<DocumentChangeDetailPage>(With("Data", DetailPageFixture.Change(changeDate: new(2026, 10, 15), passages: 12))), ["Example Agency", "The agency cut", "A shorter notice", "The text alone"]),
            ("brief empty", Page<BriefDetailPage>(With("Data", brief with { Label = null, TextOrigin = PublishedTextOrigin.Person, ApprovedAtUtc = null, FirstPublishedAtUtc = null })), ["The week in Congress", "First paragraph", "Kelly announces"]),
            ("official", Page<OfficialProfilePage>(With("Data", official, ("BrowserApiUrl", "https://api.civic-lens.test"))), ["Ann Able", "House passes", "statement about", "H.R. 40", "Referred to"]),
            ("official members", Page<OfficialMembersFragment>(With("Data", zipCompare, ("OfficialId", "pat"))), ZipRecordData),
            ("president", Page<PresidentPage>(With("Data", president)), ["Eve East", "Housing Costs", "Women", "Seafood"]),
            ("issue", Page<IssuePage>(With("Data", issue)), ["Federal firearms law", "Background checks", "Crime statements", "firearms bill", "H.R. 1 would"]),
            ("statement", Page<StatementDetailPage>(With("Data", DetailPageFixture.Statement())), ["Kelly announces", "A model wrote", "Mark Kelly", "Adam Schiff"]),
            ("document change", Page<DocumentChangeDetailPage>(With("Data", DetailPageFixture.Change())), ["Example Agency", "The agency cut", "A shorter notice", "The text alone"]),
            ("executive action", Page<ExecutiveActionDetailPage>(With("Data", DetailPageFixture.Action())), ["Implementing the Sanctioning", "Donald Trump", "2026-21599", "91 FR"]),
            ("executive action summary", Page<ExecutiveActionDetailPage>(With("Data", DetailPageFixture.Action() with { Summary = new(PublishedTextOrigin.Model, "AI summary label", true, ["The order sets sanctions."]) })),
                ["Implementing the Sanctioning", "Donald Trump", "2026-21599", "91 FR", "AI summary label", "sets sanctions"]),
            ("brief", Page<BriefDetailPage>(With("Data", brief)), ["The week in Congress", "First paragraph", "Kelly announces"]),
            ("withdrawal", Page<PublishedWithdrawalPage>(With("Withdrawal", WithdrawalNoticePageTests.Notice)), ["The quote came"]),
            ("report", Page<PublishedReportPage>(new() { ["RootPath"] = Root, ["PublicApiUrl"] = "https://api.civic-lens.test", ["TurnstileSiteKey"] = "key" }), []),
            ("report off", Page<PublishedReportPage>(new() { ["RootPath"] = Root }), []),
            ("search", Page<SearchResultsPage>(With("Data", search)), ["5334", "Panetta", "Sanctioning", "impose sanctions", "Housing"]),
            ("search empty", Page<SearchResultsPage>(With("Data", search with { Query = new("zzqxv", null, null, null, null, null), Officials = [], Bill = null, Hits = [], NextCursor = null })), ["zzqxv"]),
            ("search none", Page<SearchResultsPage>(With("Data", search with { Query = new(null, null, null, null, null, null), Officials = [], Bill = null, Hits = [], NextCursor = null })), []),
            ("glossary", Page<PublishedGlossaryPage>(new() { ["RootPath"] = Root }), []),
            ("about", Page<AboutPage>(new() { ["RootPath"] = Root }), []),
            ("method", Page<MethodPage>(new() { ["RootPath"] = Root }), []),
            ("briefs", Page<BriefsPage>(With("Data", new BriefsPageData(7, new(2026, 11, 6, 14, 0, 0, TimeSpan.Zero), 1, 1, [new(new string('b', 64), new(2026, 11, 2), "The week in Congress", "An AI draft from reviewed records.", new DateTimeOffset(2026, 11, 6, 14, 0, 0, TimeSpan.Zero))]))), ["The week in Congress"]),
            ("briefs empty", Page<BriefsPage>(With("Data", new BriefsPageData(7, new(2026, 11, 6, 14, 0, 0, TimeSpan.Zero), 1, 0, []))), []),
            ("issues index", Page<IssuesIndexPage>(With("Data", new IssuesIndexData(7, [new("firearms", "Firearms", 119, 3, 186)]))), ["Firearms"]),
            ("issues index empty", Page<IssuesIndexPage>(With("Data", new IssuesIndexData(7, []))), []),
            ("official votes", Page<OfficialRecordPage>(With("Data", new OfficialRecordPageData(7, "p1", "Ann Able", PublishedOfficeKind.Representative, [new(OfficialRecordKind.Votes, 1), new(OfficialRecordKind.Bills, 0)], OfficialRecordKind.Votes) { Votes = [new SharedVoteRowData(new string('b', 64), new(2026, 9, 16), 119, 2, 410, "House agrees to Senate changes", null, "Vote to pass or agree to", "Passed", [])], NextCursor = "next" })), ["Ann Able", "House agrees"]),
            ("official executive", Page<OfficialRecordPage>(With("Data", new OfficialRecordPageData(7, "p4", "Eve East", PublishedOfficeKind.Executive, [new(OfficialRecordKind.ExecutiveActions, 1)], OfficialRecordKind.ExecutiveActions) { ExecutiveActions = [new(new string('3', 64), "Seafood Month", PublishedExecutiveActionKind.Proclamation, new(2026, 10, 22))], Offices = [new("President", "Republican", new(2025, 1, 20), null)] })), ["Eve East", "Seafood Month"]),
            ("official bills", Page<OfficialRecordPage>(With("Data", new OfficialRecordPageData(7, "p1", "Ann Able", PublishedOfficeKind.Representative, [new(OfficialRecordKind.Votes, 1), new(OfficialRecordKind.Bills, 1), new(OfficialRecordKind.Statements, 1)], OfficialRecordKind.Bills) { Bills = [new(new string('e', 64), "hr", "H.R. 40 would study reparations.", "Commission to Study Reparation Proposals Act", new(2025, 1, 3), "Referred to the House Committee on the Judiciary.", new(2025, 1, 3))] })), ["H.R. 40", "Reparation", "Referred to"]),
            ("official statements", Page<OfficialRecordPage>(With("Data", new OfficialRecordPageData(7, "p1", "Ann Able", PublishedOfficeKind.Representative, [new(OfficialRecordKind.Votes, 1), new(OfficialRecordKind.Statements, 1)], OfficialRecordKind.Statements) { Statements = [new(new string('d', 64), new(2026, 9, 12), "A statement about H.R. 5334", "AI statement label")], LaterPage = true })), ["statement about", "AI statement label"]),
            ("vote list", Page<VoteListPage>(With("Data", voteList)), ListRecordData),
            ("vote list filtered empty", Page<VoteListPage>(With("Data", voteList with { Votes = [], NextCursor = null, Filter = RecordListFilter.Of(new(2026, 1, 1), null, ("chamber", "house")) })), ListRecordData),
            ("vote list release empty", Page<VoteListPage>(With("Data", voteList with { VotesInRelease = 0, Votes = [], NextCursor = null })), ListRecordData),
            ("vote list refused", Page<VoteListPage>(With("Data", voteList with { Votes = [], NextCursor = null, Filter = RecordListFilter.Refused("from", ("from", "2026-13-40")) })), ListRecordData),
            ("bill list", Page<BillListPage>(With("Data", billList)), ListRecordData),
            ("bill list filtered empty", Page<BillListPage>(With("Data", billList with { Bills = [], NextCursor = null, Filter = RecordListFilter.Of(null, null, ("sponsor", "zzq")) })), ListRecordData),
            ("bill list release empty", Page<BillListPage>(With("Data", billList with { Congress = null, Total = 0, Bills = [], NextCursor = null })), ListRecordData),
            ("bill list refused", Page<BillListPage>(With("Data", billList with { Bills = [], NextCursor = null, Filter = RecordListFilter.Refused("bill", ("bill", "abc")) })), ListRecordData),
            ("statement list", Page<StatementListPage>(With("Data", statementList)), ListRecordData),
            ("statement list filtered empty", Page<StatementListPage>(With("Data", statementList with { Statements = [], NextCursor = null, Filter = RecordListFilter.Of(null, null, ("official", "zzq")) })), ListRecordData),
            ("statement list release empty", Page<StatementListPage>(With("Data", statementList with { StatementsInRelease = 0, Statements = [], NextCursor = null })), ListRecordData),
            ("statement list refused", Page<StatementListPage>(With("Data", statementList with { Statements = [], NextCursor = null, Filter = RecordListFilter.Refused("from", ("from", "2026-10-01"), ("to", "2026-09-01")) })), ListRecordData),
            ("executive action list", Page<ExecutiveActionListPage>(With("Data", actionList)), ListRecordData),
            ("executive action list filtered empty", Page<ExecutiveActionListPage>(With("Data", actionList with { Actions = [], NextCursor = null, Type = PublishedExecutiveActionKind.Notice, Filter = RecordListFilter.Of(null, null, ("type", "notice")) })), ListRecordData),
            ("executive action list release empty", Page<ExecutiveActionListPage>(With("Data", actionList with { ActionsInRelease = 0, Actions = [], NextCursor = null })), ListRecordData),
            ("executive action list refused", Page<ExecutiveActionListPage>(With("Data", actionList with { Actions = [], NextCursor = null, Filter = RecordListFilter.Refused("to", ("to", "soon")) })), ListRecordData),
            ("vote corrected", Page<VoteDetailPage>(With("Data", VoteDetailPageFixture.Vote() with { Corrections = [PublishedCorrectionBannerTests.Newest] }, ("PagePath", VoteDetailPageFixture.PagePath), ("Filter", new VoteMemberFilter()))), [.. VoteRecordData, PublishedCorrectionBannerTests.Newest.Note!]),
            ("bill corrected without note", Page<BillDetailPage>(With("Data", bill with { Corrections = [PublishedCorrectionBannerTests.Newest with { Note = null }] })), ["Pat One", "Ann Two", "Cy Four", "Example Act", "headline", "tax credit", "House agrees", "imposes sanctions", "International Affairs"]),
            ("server shell", Shell<AboutPage>(new() { ["RootPath"] = Root }), []),
            ("not found", Page<PublishedNotFoundPage>(new() { ["RootPath"] = Root }), []),
            ("zip cards", Page<ZipMemberCards>(With("Data", zip)), ["Pat Member"]),
            ("zip compare", Page<ZipMemberCards>(With("Data", zipCompare, ("View", "compare"))), ZipRecordData),
            ("zip issue", Page<IssueMemberVotes>(With("Data", zipCompare)), ZipRecordData),
            ("bill members", Page<BillMembersFragment>(With("Data", billMembers)), ZipRecordData),
        ];
    }

    private static double FeaturedModelGrade(IDocument page)
    {
        var modelSection = Assert.Single(page.QuerySelectorAll(".home-featured"));
        var headline = Assert.Single(modelSection.QuerySelectorAll("h2"));
        var summaryContainer = Assert.Single(modelSection.QuerySelectorAll(".ai-summary.ai-summary-ai"));
        var summary = Assert.Single(summaryContainer.QuerySelectorAll("p"));
        return PassageGrade(headline, summary);
    }

    private static double PassageGrade(IElement heading, IElement body)
    {
        var pieces = new[] { NormalizeText(heading), NormalizeText(body) };
        Assert.All(pieces, piece => Assert.NotEmpty(piece));
        return ReadingGrade.RoundedPassage(pieces);
    }

    private static string NormalizeText(IElement element) =>
        string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static async Task<IDocument> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
