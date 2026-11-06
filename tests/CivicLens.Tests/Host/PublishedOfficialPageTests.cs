using CivicLens.Application.Actions;
using CivicLens.Application.Issues;
using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

public sealed class PublishedOfficialPageTests
{
    [Fact]
    public async Task AnActionRowShowsTheBillLineAndTheTableSaysItsSourceOnceWhileOtherRowsShowNone()
    {
        var page = Current(
            [Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)), Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)) with { BillLine = "This bill funds rent aid." }],
            [Vote('3', new DateOnly(2026, 4, 1))]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal(["This bill funds rent aid."], document.QuerySelectorAll(".bill-line").Select(line => line.TextContent));
        Assert.Equal(1, html.Split("From the Congressional Research Service summary.").Length - 1);
    }

    [Fact]
    public async Task AnOfficialRecordPageWithNoBillLineShowsNoSourceNote()
    {
        var page = Current([Entry('1', PublishedRecordKind.Action, new DateOnly(2026, 5, 1))], [Vote('3', new DateOnly(2026, 4, 1))]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("From the Congressional Research Service summary.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCurrentPageListsStatementsActionsAndVotesNewestFirstWithTheirOwnDates()
    {
        var page = Current(
            [Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)), Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1))],
            [Vote('3', new DateOnly(2026, 4, 1)), Vote('4', new DateOnly(2025, 12, 1))]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var records = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='record-title']")!;
        Assert.Equal([new string('2', 64), new string('3', 64), new string('1', 64), new string('4', 64)],
            records.QuerySelectorAll("a.cell-title").Select(link => link.GetAttribute("href")!.Replace("../", "").Replace("records/", "").Replace(".html", "")));
        Assert.Equal(["Bill or resolution, 1 May 2026", "Vote, 1 Apr 2026", "Statement, 1 Mar 2026", "Vote, 1 Dec 2025"], records.QuerySelectorAll("tbody .record-kind").Select(line => line.TextContent));
        Assert.Contains("House roll call 100, Congress 119, session 1, H.R. 7. Result of the vote: Passed", records.TextContent, StringComparison.Ordinal);
        Assert.Equal(["Yes (Yea)", "Yes (Yea)"], records.QuerySelectorAll("tbody .cast-pill").Select(pill => pill.TextContent));
    }

    [Fact]
    public async Task AVoteRowShowsThePlainResultFirstAndTheSourceWordsInBracketsSoTheReaderNeverSeesOnlyCongressWording()
    {
        var page = Current([], [Vote('3', new DateOnly(2026, 4, 1)) with { Result = "Cloture Motion Agreed to" }]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains("Result of the vote: Passed (Cloture Motion Agreed to)", new HtmlParser().ParseDocument(html).Body!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASponsoredBillRowShowsTheActionDateTheReadDateAndThePlainMeaningOfItsLatestAction()
    {
        var bill = Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1));
        var page = Current([bill], []) with { BillStatuses = Statuses(bill, "Referred to the Committee on Armed Services.", "hr") };

        var records = await RecordPanelAsync(page, "record-title");

        Assert.Equal("Latest official action on 28 Jan 2026 (read from Congress.gov on 30 Jan 2026): In committee: sent to the Committee on Armed Services.",
            records.QuerySelector("tbody .record-detail")!.TextContent);
    }

    [Theory]
    [InlineData("Received in the Senate.", "hr", "Passed the House. Now in the Senate.")]
    [InlineData("Received in the House.", "s", "Passed the Senate. Now in the House.")]
    public async Task AReceivedActionNamesTheChamberThatPassedTheBillFromTheBillTypeOfTheRowSoTheLineSaysWhereTheBillIs(string text, string billType, string meaning)
    {
        var bill = Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1));
        var page = Current([bill], []) with { BillStatuses = Statuses(bill, text, billType) };

        var records = await RecordPanelAsync(page, "record-title");

        Assert.Equal($"Latest official action on 28 Jan 2026 (read from Congress.gov on 30 Jan 2026): {meaning}", records.QuerySelector("tbody .record-detail")!.TextContent);
    }

    [Fact]
    public async Task ABillWithNoKnownMeaningShowsTheOfficialTextOnceAfterTheLeadSoTheLeadIsNotSaidTwice()
    {
        var bill = Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1));
        var page = Current([bill], []) with { BillStatuses = Statuses(bill, "Introduced in House", "hr") };

        var records = await RecordPanelAsync(page, "record-title");

        Assert.Equal("Latest official action on 28 Jan 2026 (read from Congress.gov on 30 Jan 2026): Introduced in House.", records.QuerySelector("tbody .record-detail")!.TextContent);
    }

    [Fact]
    public async Task ABillWithNoStoredStatusAndOtherRecordsShowNoStatusLine()
    {
        var page = Current([Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)), Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []);

        var records = await RecordPanelAsync(page, "record-title");

        Assert.DoesNotContain("Latest official action", records.TextContent, StringComparison.Ordinal);
    }

    private static Dictionary<string, ActionBillStatus> Statuses(PublishedRecordEntry bill, string text, string billType) =>
        new() { [bill.RecordId] = new(new() { RecordId = bill.RecordId, Text = text, Date = new DateOnly(2026, 1, 28), ObservedAtUtc = new DateTimeOffset(2026, 1, 30, 9, 0, 0, TimeSpan.Zero) }, billType) };

    [Fact]
    public async Task AnExecutiveActionRowShowsItsSpecificTypeAndOtherRowsShowTheirKind()
    {
        var page = Current(
            [Entry('1', PublishedRecordKind.ExecutiveAction, new DateOnly(2026, 5, 1)) with { DocumentType = PublishedExecutiveActionKind.Proclamation },
             Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 4, 1))], []);

        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var kinds = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='record-title']")!.QuerySelectorAll("tbody .record-kind")
            .Select(line => line.TextContent.Split(',')[0]);
        Assert.Equal(["Proclamation", "Bill or resolution"], kinds);
    }

    [Fact]
    public async Task AnOfficialPageAndAFacetPageOrderTheSameEntriesTheSameWayBecauseOneRuleOwnsTheDateOrder()
    {
        var published = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        PublishedRecordEntry[] entries =
        [
            Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)),
            Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)) with { EventDate = null, FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero) },
            Entry('3', PublishedRecordKind.Statement, new DateOnly(2026, 5, 2)) with { EventDate = null, FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 2, 21, 0, 0, TimeSpan.FromHours(-5)) },
            Entry('4', PublishedRecordKind.Action, new DateOnly(2026, 4, 1)),
            Entry('0', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)) with { EventDate = null, FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 2, 15, 0, 0, TimeSpan.Zero) }
        ];
        var facet = ReleaseFacets.From(new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = 1,
            PublishedAtUtc = published,
            Withdrawals = [],
            Records = [.. entries.Select(entry => entry with { IssueIds = ["housing"] })]
        }, PublicationCatalogs.None).Issues.Single();

        var official = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(Current(entries, []), PublicationCatalogs.None, CancellationToken.None);
        var facetHtml = await new ReleaseRenderer(null, null).RenderFacetPageAsync(facet, 1, ReleaseBillLines.None, CancellationToken.None);

        string[] Order(string html) => [.. new HtmlParser().ParseDocument(html).QuerySelectorAll("tbody a.cell-title")
            .Select(link => link.GetAttribute("href")!.Replace("../", "").Replace("records/", "").Replace(".html", ""))];
        Assert.Equal(Order(official), Order(facetHtml));
        Assert.Equal([new string('3', 64), new string('0', 64), new string('2', 64), new string('4', 64), new string('1', 64)], Order(official));
    }

    [Fact]
    public async Task AVoteFollowsTheRecordsOfItsDayBecauseAVoteRowHasNoPublicationTime()
    {
        var page = Current([Entry('a', PublishedRecordKind.Action, new DateOnly(2026, 5, 2))], [Vote('0', new DateOnly(2026, 5, 2))]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None);

        Assert.Equal([new string('a', 64), new string('0', 64)], new HtmlParser().ParseDocument(html).QuerySelectorAll("tbody a.cell-title")
            .Select(link => link.GetAttribute("href")!.Replace("../", "").Replace("records/", "").Replace(".html", "")));
    }

    [Fact]
    public async Task TheCurrentPageShowsTheNameTheOfficesAndTheDatedTermsWithTableCaptionsAndAnOngoingTermAsPresent()
    {
        var page = Current([], []) with
        {
            Profile = new PublishedOfficialProfile
            {
                Id = "mayor",
                Name = "Mayor Example",
                Terms =
                [
                    new() { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 1, Party = "Democrat", Start = new(1993, 1, 3), End = new(2025, 1, 3) },
                    new() { Office = "Secretary of State", Kind = PublishedOfficeKind.Executive, Start = new(2025, 1, 21) }
                ]
            }
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("Mayor Example", document.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Representative for WA, district 1", "Secretary of State"],
            document.QuerySelectorAll("[aria-labelledby='panel-offices'] tbody tr td:first-child").Select(cell => cell.TextContent));
        Assert.Equal(["3 Jan 2025", "Present"], document.QuerySelectorAll("[aria-labelledby='panel-offices'] tbody tr td[data-label='To']").Select(cell => cell.TextContent));
        Assert.All(document.QuerySelectorAll("table"), table => Assert.NotNull(table.QuerySelector("caption")));
        Assert.Contains("href=\"../officials.html\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../assets/css/theme.css\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLedeNamesTheRolePartyAndTenureSoAReaderKnowsWhoThisIsWithoutReadingTheTable()
    {
        var page = Current([], []) with
        {
            Profile = Profile("rep", "Rita Rep", new PublishedOfficeTerm { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 9, Party = "Democrat", Start = new(1997, 1, 7) })
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var header = new HtmlParser().ParseDocument(html).QuerySelector("header.page-header")!;
        Assert.Equal("Democratic representative for WA, district 9", header.QuerySelector(".page-header-lede")!.TextContent);
        Assert.Equal("In the House since January 1997.", header.QuerySelector(".page-header-meta span")!.TextContent);
    }

    [Fact]
    public async Task TheRecordsCountLineCountsEachKindAndTheVotesWithTheirCongressSoNoCountHasAnUnstatedScope()
    {
        var page = Current(
            [Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)), Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)), Entry('5', PublishedRecordKind.Action, new DateOnly(2026, 5, 2))],
            [Vote('3', new DateOnly(2026, 4, 1)), Vote('4', new DateOnly(2025, 12, 1))]);

        var records = await RecordPanelAsync(page, "record-title");

        Assert.Equal("This list holds 5 published records: 1 statement, 2 bills and resolutions, and 2 votes in the 119th Congress. Rows are newest first.", records.QuerySelector("p.muted")!.TextContent);
    }

    [Fact]
    public async Task TheOfficesPanelComesLastAndMergesTermsIntoOneRowPerUnbrokenStretchWithANote()
    {
        var page = Current([], []) with
        {
            Profile = Profile("rep", "Rita Rep",
                new PublishedOfficeTerm { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 9, Party = "Democrat", Start = new(1997, 1, 7), End = new(1999, 1, 3) },
                new PublishedOfficeTerm { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 9, Party = "Democrat", Start = new(1999, 1, 6), End = new(2001, 1, 3) })
        };

        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None));

        var offices = document.QuerySelector("[aria-labelledby='panel-offices']")!;
        Assert.Same(offices, document.QuerySelectorAll("main section.panel").Last());
        Assert.Equal(["7 Jan 1997"], offices.QuerySelectorAll("tbody td[data-label='From']").Select(cell => cell.TextContent));
        Assert.Equal(["3 Jan 2001"], offices.QuerySelectorAll("tbody td[data-label='To']").Select(cell => cell.TextContent));
        Assert.Contains("Each row is one unbroken stretch in the same office.", offices.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRecordsCountLineUsesTheSingularForOneRecordAndItsOwnKind()
    {
        var records = await RecordPanelAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []), "record-title");

        Assert.Equal("This list holds 1 published record: 1 statement. Rows are newest first.", records.QuerySelector("p.muted")!.TextContent);
    }

    [Fact]
    public async Task AnEmptyRecordsTableHasNoCountLineBecauseItsEmptyStateAlreadySaysThereAreNoRecords()
    {
        var records = await RecordPanelAsync(Current([], []), "record-title");

        Assert.Null(records.QuerySelector("p.muted"));
        Assert.Contains("There are no records here.", records.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEarlierCongressPageCountsItsVotesWithTheSameCongressWordingAsItsLede()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 117, false, [], [], [Vote('3', new DateOnly(2022, 4, 1)) with { Congress = 117 }], ListPublishedAtUtc);

        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None));

        Assert.Equal("Votes in the 117th Congress.", document.QuerySelector(".page-header-lede")!.TextContent);
        Assert.Equal("This list holds 1 published record: 1 vote in the 117th Congress. Rows are newest first.", document.QuerySelector("[aria-labelledby='record-title'] p.muted")!.TextContent);
    }

    [Fact]
    public async Task TermsInTheSameSeatThatFollowEachOtherMakeOneRowEvenWhenTheNextOneStartsAfterTheLateSwearingInOfJanuary1997()
    {
        var rows = await OfficeRowsAsync(Seat(new(1993, 1, 5), new(1995, 1, 3)), Seat(new(1995, 1, 4), new(1997, 1, 3)), Seat(new(1997, 1, 7), new(2027, 1, 3)));

        Assert.Equal(["5 Jan 1993 to 3 Jan 2027"], rows);
    }

    [Fact]
    public async Task ATermThatStartsEightDaysAfterTheLastEndedStartsANewRowBecauseNoSwearingInIsThatLate()
    {
        var rows = await OfficeRowsAsync(Seat(new(2001, 1, 3), new(2003, 1, 3)), Seat(new(2003, 1, 11), new(2005, 1, 3)));

        Assert.Equal(["3 Jan 2001 to 3 Jan 2003", "11 Jan 2003 to 3 Jan 2005"], rows);
    }

    [Theory]
    [InlineData("2003-01-03", "2003-06-10")]
    [InlineData("2005-03-01", "2005-09-01")]
    public async Task AnOfficialWhoLeftOfficeAndCameBackInsideOneCongressGetsTwoRowsBecauseTheStretchWasBroken(string endedOn, string returnedOn)
    {
        var rows = await OfficeRowsAsync(Seat(new(2001, 1, 3), DateOnly.Parse(endedOn)), Seat(DateOnly.Parse(returnedOn), new(2007, 1, 3)));

        Assert.Equal(2, rows.Length);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("district")]
    [InlineData("party")]
    [InlineData("office")]
    public async Task ATermWithADifferentStateDistrictPartyOrOfficeStartsANewRowBecauseTheRowNamesOneSeat(string changed)
    {
        var next = Seat(new(2003, 1, 3), new(2005, 1, 3));
        next = changed switch
        {
            "state" => next with { State = "OR" },
            "district" => next with { District = 2 },
            "party" => next with { Party = "Republican" },
            _ => next with { Office = "sen", Kind = PublishedOfficeKind.Senator, District = null }
        };

        var rows = await OfficeRowsAsync(Seat(new(2001, 1, 3), new(2003, 1, 3)), next);

        Assert.Equal(2, rows.Length);
    }

    [Fact]
    public async Task TheOfficesNoteNamesTheRosterSourceOnlyWhenEveryTermComesFromTheRoster()
    {
        var roster = await OfficialAsync(Seat(new(2001, 1, 3), null));
        var configured = await OfficialAsync(new PublishedOfficeTerm { Office = "Mayor", Kind = PublishedOfficeKind.Other, Start = new(2025, 1, 20) });

        Assert.Contains("Source: congress-legislators.", roster.QuerySelector("[aria-labelledby='panel-offices'] p.muted")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("Each row is one unbroken stretch in the same office.", configured.QuerySelector("[aria-labelledby='panel-offices'] p.muted")!.TextContent);
    }

    [Fact]
    public async Task TheTenureStartsAtTheStartOfTheUnbrokenStretchInTheChamberEvenAfterADistrictChange()
    {
        var tenure = await TenureAsync(Seat(new(1997, 1, 7), new(1999, 1, 3)), Seat(new(1999, 1, 6), new(2001, 1, 3)) with { District = 8 }, Seat(new(2001, 1, 3), new(2027, 1, 3)));

        Assert.Equal("In the House since January 1997.", tenure);
    }

    [Fact]
    public async Task ASenatorWhoLeftTheHouseDatesTheTenureFromTheSenateBecauseTheTenureNamesOneChamber()
    {
        var terms = new[] { Seat(new(2001, 1, 3), new(2007, 1, 3)), Senate(new(2007, 1, 4), new(2013, 1, 3)), Senate(new(2013, 1, 3), new(2027, 1, 3)) };

        Assert.Equal("Democratic senator for WA", await LedeAsync(terms));
        Assert.Equal("In the Senate since January 2007.", await TenureAsync(terms));
    }

    [Fact]
    public async Task AFormerMemberGetsAClosedTenureAndFormerBecauseSinceWouldBeFalse()
    {
        var terms = new[] { Seat(new(2001, 1, 3), new(2003, 1, 3)), Seat(new(2003, 1, 3), new(2005, 1, 3)) };

        Assert.Equal("Former Democratic representative for WA, district 9", await LedeAsync(terms));
        Assert.Equal("In the House from January 2001 to January 2005.", await TenureAsync(terms));
    }

    [Fact]
    public async Task ATermThatStartsAfterTheReleaseDateIsNotTheLatestTermSoTheLedeDescribesTheOfficeTheyHoldToday()
    {
        var terms = new[] { Seat(new(2025, 1, 3), new(2027, 1, 3)), Seat(new(2027, 1, 3), null) with { District = 3 } };

        Assert.Equal("Democratic representative for WA, district 9", await LedeAsync(terms));
        Assert.Equal("In the House since January 2025.", await TenureAsync(terms));
    }

    [Fact]
    public async Task AnExecutiveHasNoTenureSentenceBecauseTenureNamesAChamber()
    {
        var term = new PublishedOfficeTerm { Office = "President", Kind = PublishedOfficeKind.Executive, Start = new(2025, 1, 20) };

        Assert.Equal("President", await LedeAsync(term));
        Assert.Null(await TenureAsync(term));
    }

    [Fact]
    public async Task AnOfficialWithNoTermHasNoLedeSoTheHeaderShowsNoEmptyLine()
    {
        Assert.Null(await LedeAsync());
    }

    [Fact]
    public async Task ACardIgnoresATermThatStartsAfterTheReleaseDateSoTheListMatchesThePage()
    {
        var profile = Profile("a", "Ann Able", Term("rep", "MA", "Democratic"), Term("sen", "MA", "Democratic") with { Start = new DateOnly(2027, 1, 3) });

        var card = (await ListAsync(new OfficialListing(profile, 1))).QuerySelector("ul.officials > li")!;

        Assert.Equal("representative", card.GetAttribute("data-office"));
    }

    [Fact]
    public async Task AnOfficialWithNoPartyOnAnyTermGetsNoPartyColumnBecauseItWouldBeEmpty()
    {
        var page = Current([], []) with { Profile = Profile("pres", "Pat President", new PublishedOfficeTerm { Office = "President", Kind = PublishedOfficeKind.Executive, Start = new DateOnly(2025, 1, 20) }) };

        var offices = await PanelTableAsync(page, "panel-offices");

        Assert.Equal(["Office", "From", "To"], offices.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.Equal(3, offices.QuerySelectorAll("tbody td").Length);
    }

    [Fact]
    public async Task AnOfficialWithAPartyOnOneTermKeepsThePartyColumnForEveryTerm()
    {
        var page = Current([], []) with
        {
            Profile = Profile("a", "Ann Able", Term("sen", "MA", "Democratic"), new PublishedOfficeTerm { Office = "President", Kind = PublishedOfficeKind.Executive, Start = new DateOnly(2030, 1, 20) })
        };

        var offices = await PanelTableAsync(page, "panel-offices");

        Assert.Equal(["Office", "Party", "From", "To"], offices.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.All(offices.QuerySelectorAll("tbody tr"), row => Assert.Equal(4, row.QuerySelectorAll("td").Length));
    }

    [Fact]
    public async Task APageWithNoCastOnAnyRowGetsNoCastColumnBecauseOnlyAVoteHasACast()
    {
        var records = await RecordPanelTableAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []), "record-title");

        Assert.Equal(["Record"], records.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.Equal(1, records.QuerySelectorAll("tbody td").Length);
    }

    [Fact]
    public async Task APageWithOneVoteKeepsTheCastColumnForEveryRow()
    {
        var records = await RecordPanelTableAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], [Vote('3', new DateOnly(2026, 4, 1))]), "record-title");

        Assert.Equal(["Record", "Cast"], records.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.All(records.QuerySelectorAll("tbody tr"), row => Assert.Equal(2, row.QuerySelectorAll("td").Length));
    }

    [Fact]
    public async Task TheHeaderHoldsTheNameThePortraitAndTheLinksRowSoTheyReadAsOneIntroduction()
    {
        var page = Current([], []) with
        {
            Profile = WithPhoto("rep", "Rita Rep", "Public domain.") with
            {
                Website = "https://rep.example.gov/home",
                Terms = [new PublishedOfficeTerm { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 9, Party = "Democrat", Start = new(1997, 1, 7) }]
            }
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var header = new HtmlParser().ParseDocument(html).QuerySelector("header.page-header")!;
        Assert.Equal("Rita Rep", header.QuerySelector("h1")!.TextContent);
        Assert.NotNull(header.QuerySelector(".page-header-portrait figure.portrait img"));
        Assert.Equal("Democratic representative for WA, district 9", header.QuerySelector(".page-header-lede")!.TextContent);
        Assert.Equal("Official website", header.QuerySelector(".page-header-meta a[href='https://rep.example.gov/home']")!.TextContent);
    }

    [Fact]
    public async Task APastCongressHeaderHasNoPortraitOrLinksRow()
    {
        var past = new ReleaseOfficialPage(Profile() with { Website = "https://mayor.example.gov" }, true, 118, false, [], [], [], ListPublishedAtUtc);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(past, PublicationCatalogs.None, CancellationToken.None);

        var header = new HtmlParser().ParseDocument(html).QuerySelector("header.page-header")!;
        Assert.Null(header.QuerySelector(".page-header-portrait"));
        Assert.Null(header.QuerySelector(".page-header-meta"));
    }

    [Fact]
    public async Task TheCurrentPageLinksTheOfficialWebsiteNamingItsHostWithNoReferrerAndNoOpener()
    {
        var page = Current([], []) with { Profile = Profile() with { Website = "https://warren.senate.gov:8443/about?a=1" } };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var link = new HtmlParser().ParseDocument(html).QuerySelector("a[href='https://warren.senate.gov:8443/about?a=1']")!;
        Assert.Equal("Official website", link.TextContent);
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Equal("warren.senate.gov", link.NextElementSibling!.TextContent);
        Assert.Equal("muted small", link.NextElementSibling.ClassName);
    }

    [Fact]
    public async Task TheCurrentPageShowsThePortraitFromTheReleaseRootWithItsSizeAltTextAndCredit()
    {
        var hash = new string('c', 64);
        var photo = new PublishedPhoto
        {
            Path = PublicationProtocol.PhotoPath(hash),
            Credit = "Official portrait. Public domain.",
            SourceUrl = "https://example.gov/images/mayor.jpg",
            Width = 225,
            Height = 275
        };
        var page = Current([], []) with { Profile = Profile() with { Photo = photo } };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var figure = new HtmlParser().ParseDocument(html).QuerySelector("figure.portrait")!;
        var image = figure.QuerySelector("img")!;
        Assert.Equal($"../photos/{hash}.jpg", image.GetAttribute("src"));
        Assert.Equal(("225", "275", "eager"), (image.GetAttribute("width"), image.GetAttribute("height"), image.GetAttribute("loading")));
        Assert.Equal("Official portrait of Mayor Example", image.GetAttribute("alt"));
        Assert.Equal("Official portrait. Public domain.", figure.QuerySelector("figcaption")!.TextContent);
        Assert.Null(figure.QuerySelector("svg"));
    }

    [Fact]
    public async Task AnOfficialWithNoPhotoShowsAHiddenPlaceholderInTheSameFrameAndSaysSo()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        var figure = new HtmlParser().ParseDocument(html).QuerySelector("figure.portrait")!;
        Assert.Null(figure.QuerySelector("img"));
        var placeholder = figure.QuerySelector("svg")!;
        Assert.Equal(("true", "0 0 225 275"), (placeholder.GetAttribute("aria-hidden"), placeholder.GetAttribute("viewBox")));
        Assert.Equal("No portrait is shown for this official.", figure.QuerySelector("figcaption")!.TextContent);
    }

    [Fact]
    public async Task APastCongressPageShowsNoPortraitBecauseOnlyTheCurrentPageHasOne()
    {
        var past = new ReleaseOfficialPage(Profile(), true, 118, false, [], [], [], ListPublishedAtUtc);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(past, PublicationCatalogs.None, CancellationToken.None);

        Assert.Null(new HtmlParser().ParseDocument(html).QuerySelector("figure.portrait"));
    }

    [Fact]
    public async Task AnOfficialWithNoWebsiteShowsNoWebsiteLink()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("Official website", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheVotedFigureOfEachChamberNamesTheChamberAndTheCountsAndItsNoteKeepsTheApprovedMeaningOfAMissedVote()
    {
        var day = new DateOnly(2026, 4, 1);
        var votes = new[] { Vote('a', day) with { Cast = PublishedOfficialVote.NotVoting }, Vote('b', day.AddDays(2)), Vote('c', day.AddDays(1)) with { Chamber = PublishedChamber.Senate } };

        var figures = FiguresOf(await RenderedAsync(votes));

        Assert.Equal([("1 of 2", "House roll calls voted on"), ("1 of 1", "Senate roll call voted on")], figures.Take(2).Select(figure => (figure.Value, figure.Label)));
        Assert.All(figures.Take(2), figure => Assert.Equal("A missed vote means the official did not vote on it. The count includes procedural votes. It counts only the roll calls on this site, which can be fewer than the chamber held.", figure.Note));
    }

    [Fact]
    public async Task TheVotedFigureShowsAShareOnlyAtTheThresholdAndAnOfficialWithNoRowsOfAChamberGetsNoFigureForIt()
    {
        var votes = Enumerable.Range(0, 5).Select(index => Vote((char)('a' + index), new DateOnly(2026, 4, 1)) with
        { Cast = index == 0 ? PublishedOfficialVote.NotVoting : "Yea" }).ToArray();

        var figures = FiguresOf(await RenderedAsync(votes));

        Assert.Equal(("80%", "of roll calls voted on: 4 of 5"), (figures[0].Value, figures[0].Label));
        Assert.DoesNotContain(figures, figure => figure.Label.Contains("Senate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOfficialWhoMissedEveryRollCallOfAChamberShowsZeroPercentVotedSoTheFigureNeverHidesAFullAbsence()
    {
        var votes = Enumerable.Range(0, 5).Select(index => Vote((char)('a' + index), new DateOnly(2026, 4, 1).AddDays(index)) with { Cast = PublishedOfficialVote.NotVoting }).ToArray();

        var figures = FiguresOf(await RenderedAsync(votes));

        Assert.Equal(("0%", "of roll calls voted on: 0 of 5"), (figures[0].Value, figures[0].Label));
    }

    [Fact]
    public async Task TheWithPartyFigureOfEachChamberNamesWhoAndHowManySplitVotesAndTheNoteSaysHowManyWereNotCast()
    {
        var day = new DateOnly(2026, 4, 1);
        var votes = new[] { Split('a', day, "Yea"), Split('b', day.AddDays(2), "Nay"), Split('c', day.AddDays(1), "No"), Split('d', day.AddDays(3), "Present"),
            Split('e', day, "Nay") with { Chamber = PublishedChamber.Senate, Party = PublishedPartyMajority.Democrat, PartyMajority = "Nay" } };

        var figures = FiguresOf(await RenderedAsync(votes)).Where(figure => figure.Label.Contains("split vote", StringComparison.Ordinal)).ToArray();

        Assert.Equal([("1 of 3", "split votes with most House Republicans"), ("1 of 1", "split vote with most Senate Democrats")], figures.Select(figure => (figure.Value, figure.Label)));
        Assert.Equal(["A split vote is a substantive roll call vote where most Democrats and most Republicans who cast Yes or No voted on opposite sides. They did not cast Yes or No on 1 other split vote.", "A split vote is a substantive roll call vote where most Democrats and most Republicans who cast Yes or No voted on opposite sides."], figures.Select(figure => figure.Note));
    }

    [Fact]
    public async Task AnOfficialWhoNeverBrokeWithThePartyShowsAFullShareAndNoMarkAgainstItAndAnOfficialWithNoSplitVoteGetsNoPartyFigure()
    {
        var day = new DateOnly(2026, 4, 1);
        var matched = await RenderedAsync([.. Enumerable.Range(0, 5).Select(index => Split((char)('a' + index), day.AddDays(index), "Yea"))]);
        var none = await RenderedAsync([Vote('a', day)]);

        Assert.Contains(("100%", "with most House Republicans: 5 of 5 split votes"), FiguresOf(matched).Select(figure => (figure.Value, figure.Label)));
        Assert.Equal("5 votes. No mark shows a vote against most of their party on split votes.", matched.QuerySelector(".vote-strip-marks")!.GetAttribute("aria-label"));
        Assert.DoesNotContain(FiguresOf(none), figure => figure.Label.Contains("with most", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(4, "3 of 4", "split votes with most House Republicans")]
    [InlineData(5, "80%", "with most House Republicans: 4 of 5 split votes")]
    public async Task ThePercentOfVotesWithTheirPartyShowsOnlyAtTheThreshold(int cast, string value, string label)
    {
        var votes = Enumerable.Range(0, cast).Select(index => Split((char)('a' + index), new DateOnly(2026, 4, 1).AddDays(index), index == 0 ? "Nay" : "Yea"));

        var figures = FiguresOf(await RenderedAsync([.. votes]));

        Assert.Contains((value, label), figures.Select(figure => (figure.Value, figure.Label)));
    }

    [Fact]
    public async Task AnOfficialWhoseRowsNameNeitherMajorPartyGetsNoWithPartyFigure()
    {
        var document = await RenderedAsync([Split('a', new DateOnly(2026, 4, 1), "Yea") with { Party = "Independent" }]);

        Assert.DoesNotContain(FiguresOf(document), figure => figure.Label.Contains("with most", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyARowOfASplitVoteCastAgainstMostOfTheirPartyIsMarkedAsAgainstTheParty()
    {
        var day = new DateOnly(2026, 4, 1);
        var votes = new[] { Split('a', day, "Nay"), Split('b', day.AddDays(1), "Yea"), Split('c', day.AddDays(2), "Present"),
            Vote('d', day.AddDays(3)) with { Cast = "Nay", Kind = PublishedVoteKind.Cloture }, Vote('e', day.AddDays(4)) with { Cast = "Nay", PartyMajority = null } };

        var document = await RecordRenderedAsync(votes);

        var marked = document.QuerySelectorAll("[aria-labelledby='record-title'] tbody tr.against-party");
        Assert.EndsWith(PublicationProtocol.RecordPagePath(Vote('a', day).RecordId), Assert.Single(marked).QuerySelector("a")!.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Equal(5, document.QuerySelectorAll("[aria-labelledby='record-title'] tbody tr").Length);
    }

    [Fact]
    public async Task TheKindTabsCountEveryRowOfTheListSoATabNeverPromisesMoreThanItShows()
    {
        var document = await RecordRenderedWithCountsAsync();

        var tabs = document.QuerySelectorAll("[data-records] [data-record-filters] [data-filter-value]").ToArray();
        Assert.Equal(["All 9", "Statements 4", "Bills 2", "Votes 3", "Issues they work on"], tabs.Select(TabText));
        foreach (var tab in tabs.Skip(1).Take(3))
        {
            var rows = document.QuerySelectorAll($"[data-records] tbody tr.{tab.GetAttribute("data-filter-value")}");
            Assert.Equal(int.Parse(tab.QuerySelector(".segmented-count")!.TextContent), rows.Length);
        }
    }

    [Fact]
    public async Task TheKindTabsAreHiddenWithoutScriptAndNoRowIsHiddenSoTheListStaysWhole()
    {
        var document = await RecordRenderedWithCountsAsync();

        Assert.NotNull(document.QuerySelector("[data-records] [data-record-filters][hidden]"));
        Assert.Empty(document.QuerySelectorAll("[data-records] tbody tr[hidden]"));
        Assert.Equal("", document.QuerySelector("[data-records] [data-filter-status][role='status']")!.TextContent);
    }

    [Fact]
    public async Task ARecordsListOfOneKindHasNoKindTabsBecauseTheyCouldNotNarrowItAndANoProfilePageHasNoTabAtAll()
    {
        var withProfile = await RecordRenderedAsync([Split('a', new DateOnly(2026, 4, 1), "Yea")]);
        var page = Current([], [Split('a', new DateOnly(2026, 4, 1), "Yea")]);
        var noProfile = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None));

        Assert.Equal(["All 1", "Issues they work on"], withProfile.QuerySelectorAll("[data-record-filters] [data-filter-value]").Select(TabText));
        Assert.Null(noProfile.QuerySelector("[data-record-filters]"));
    }

    [Fact]
    public async Task TheAgainstPartyTabShowsOnlyWhenTheOfficialHasAVoteAgainstTheirPartyAndCountsThoseVotes()
    {
        var day = new DateOnly(2026, 4, 1);

        var withBreak = await RecordRenderedAsync([Split('a', day, "Nay"), Split('b', day, "Yea")]);
        var withoutBreak = await RecordRenderedAsync([Split('a', day, "Yea"), Split('b', day, "Yea")]);

        Assert.Equal(["All 2", "Against their party 1", "Issues they work on"],
            withBreak.QuerySelectorAll("[aria-labelledby='record-title'] [data-filter-value]").Select(TabText));
        Assert.DoesNotContain("Against their party 1", withoutBreak.QuerySelectorAll("[data-record-filters] [data-filter-value]").Select(TabText));
        Assert.Equal(1, withBreak.QuerySelectorAll("tbody tr.against-party").Length);
    }

    [Fact]
    public async Task AnEarlierCongressPageHasNoAgainstPartyTabEvenWithAVoteAgainstTheirParty()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 118, false, [], [], [Split('a', new DateOnly(2024, 4, 1), "Nay") with { Congress = 118 }], ListPublishedAtUtc);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.NotNull(document.QuerySelector("tbody tr.against-party"));
        Assert.Null(document.QuerySelector("[data-record-filters]"));
    }

    private static (string Value, string Label, string Note)[] FiguresOf(AngleSharp.Dom.IDocument document) =>
        [.. document.QuerySelectorAll(".key-figure").Select(figure => (figure.QuerySelector(".key-figure-value")!.TextContent,
            figure.QuerySelector(".key-figure-label span")!.TextContent, figure.QuerySelector(".term-popover")!.TextContent))];

    private static Task<AngleSharp.Dom.IDocument> RenderedAsync(PublishedOfficialVote[] votes) => OfficialDocumentAsync(WithProfile(Current([], votes)));

    private static Task<AngleSharp.Dom.IDocument> RecordRenderedAsync(PublishedOfficialVote[] votes) => RecordDocumentAsync(WithProfile(Current([], votes)));

    private static ReleaseOfficialPage WithProfile(ReleaseOfficialPage page) => page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

    private static async Task<AngleSharp.Dom.IDocument> OfficialDocumentAsync(ReleaseOfficialPage page) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None));

    private static async Task<AngleSharp.Dom.IDocument> RecordDocumentAsync(ReleaseOfficialPage page) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None));

    private static PublishedOfficialVote Split(char id, DateOnly date, string cast) =>
        Vote(id, date) with { SplitVote = true, PartyMajority = PublishedPartyMajority.Yea, Cast = cast };

    [Fact]
    public async Task TheVoteStripHasOneMarkForEachRollCallOldestFirstAndMarksASplitVoteAgainstThePartyAtTheHeightOfItsCastBecauseBothAreFactsTheStripShows()
    {
        var day = new DateOnly(2026, 4, 1);
        var votes = new[] { Split('a', day, "Yea"), Vote('b', day.AddDays(2)) with { Cast = "Nay" }, Split('c', day.AddDays(1), "Nay"), Vote('d', day.AddDays(3)) with { Cast = "Present" } };

        var strip = (await RenderedAsync(votes)).QuerySelector(".vote-strip")!;

        Assert.Equal("Every vote in this Congress", strip.QuerySelector("h2")!.TextContent);
        Assert.Equal("One mark for each of 4 roll calls, from 1 Apr 2026 on the left to 4 Apr 2026 on the right.", strip.QuerySelector("p")!.TextContent);
        var marks = strip.QuerySelector(".vote-strip-marks")!;
        Assert.Equal("4 votes. 1 mark shows a vote against most House Republicans on split votes.", marks.GetAttribute("aria-label"));
        Assert.Equal(["vote-mark vote-mark-yes", "vote-mark vote-mark-no vote-mark-against", "vote-mark vote-mark-no", "vote-mark vote-mark-other"], marks.Children.Select(mark => mark.ClassName));
        Assert.Equal(["Yes", "No", "Against most of their party", "Did not vote Yes or No"], strip.QuerySelectorAll(".vote-strip-legend li").Select(item => item.TextContent));
    }

    [Fact]
    public async Task AVoteForACandidateHasItsOwnMarkAndLegendEntrySoAReaderDoesNotTakeItForAMissedVote()
    {
        var day = new DateOnly(2025, 1, 3);
        var votes = new[] { Vote('a', day) with { Cast = "Jeffries", Totals = null, BillType = null, BillNumber = null }, Vote('b', day.AddDays(1)) with { Cast = PublishedOfficialVote.Present } };

        var strip = (await RenderedAsync(votes)).QuerySelector(".vote-strip")!;

        Assert.Equal(["vote-mark vote-mark-candidate", "vote-mark vote-mark-other"], strip.QuerySelector(".vote-strip-marks")!.Children.Select(mark => mark.ClassName));
        Assert.Equal(["Yes", "No", "Against most of their party", "Voted for a candidate", "Did not vote Yes or No"],
            strip.QuerySelectorAll(".vote-strip-legend li").Select(item => item.TextContent));
    }

    [Fact]
    public async Task TheVoteStripOfOneRollCallNamesItsDayBecauseARangeOfOneDayReadsWrong()
    {
        var strip = (await RenderedAsync([Vote('a', new DateOnly(2026, 4, 1))])).QuerySelector(".vote-strip")!;

        Assert.Equal("One mark for the roll call of 1 Apr 2026.", strip.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public async Task AnOfficialWithNoVoteHasNoVoteStripBecauseItWouldDrawNothing()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []), PublicationCatalogs.None, CancellationToken.None);

        Assert.Null(new HtmlParser().ParseDocument(html).QuerySelector(".vote-strip"));
    }

    [Fact]
    public async Task EachKeyFigureHasAnInfoButtonThatNamesItAndOpensItsNoteSoNoNumberStandsWithoutItsMeaning()
    {
        var document = await RenderedWithCountsAsync();

        var section = document.QuerySelector("section.key-figures")!;
        Assert.Equal("Key figures for the 119th Congress", section.GetAttribute("aria-label"));
        var figures = section.QuerySelectorAll(".key-figure").ToArray();
        Assert.Equal(["2 of 3", "1", "3"], figures.Select(figure => figure.QuerySelector(".key-figure-value")!.TextContent));
        foreach (var figure in figures)
        {
            var button = figure.QuerySelector("button.info-button")!;
            Assert.StartsWith("What this means: " + figure.QuerySelector(".key-figure-label span")!.TextContent, button.GetAttribute("aria-label"), StringComparison.Ordinal);
            Assert.Equal(figure.QuerySelector(".term-popover")!.Id, button.GetAttribute("popovertarget"));
        }
    }

    [Fact]
    public async Task TheBillAndStatementFiguresCountTheCongressOnlyAndOnlyAZeroStatementFigureSaysNothingWasPublishedYet()
    {
        var withRecords = FiguresOf(await RenderedWithCountsAsync());
        var none = FiguresOf(await RenderedAsync([Vote('a', new DateOnly(2026, 4, 1))]));

        Assert.Equal(("1", "bill or resolution sponsored", "Counts the bills of this official in this release, in the 119th Congress."), withRecords[^2]);
        Assert.Equal(("3", "statements published so far", "Counts the statements of this official in this release, in the 119th Congress."), withRecords[^1]);
        Assert.Equal(("0", "statements published so far", "This means we have not yet published a statement from this official. It does not mean they made none."), none[^1]);
    }

    [Fact]
    public async Task ThePageHasNoKeyFiguresWithoutAProfileBecauseTheirCountsComeFromIt()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []), PublicationCatalogs.None, CancellationToken.None);

        Assert.Null(new HtmlParser().ParseDocument(html).QuerySelector(".key-figures"));
    }

    [Fact]
    public async Task TheHeaderIsInTheHeroBandAndNotInTheMainColumnSoTheNameAndPortraitSitOnTheNavyBand()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("Mayor Example", document.QuerySelector(".public-hero .official-hero header.page-header h1")!.TextContent);
        Assert.Null(document.QuerySelector("main header.page-header"));
    }

    [Fact]
    public async Task TheIssuesTabIsLastAndOpensAPaneOutsideTheRecordListSoTheListKeepsOnlyRecordRows()
    {
        var document = await RecordRenderedWithCountsAsync();

        Assert.Equal("issues", document.QuerySelectorAll("[data-record-filters] [data-filter-value]").Last().GetAttribute("data-filter-value"));
        var pane = document.QuerySelector("[data-records] [data-issues-pane='issues']")!;
        Assert.Equal("Issues they work on", pane.QuerySelector("h3")!.TextContent);
        Assert.Null(pane.Closest("[data-list-pane]"));
        Assert.Null(document.QuerySelector("[data-list-pane] [data-issues-pane]"));
        Assert.Equal(1, document.QuerySelectorAll("[data-list-pane] table").Length);
    }

    [Theory]
    [InlineData("Yea", "Yes (Yea)", "cast-pill cast-yes")]
    [InlineData("Aye", "Yes (Aye)", "cast-pill cast-yes")]
    [InlineData("Nay", "No (Nay)", "cast-pill")]
    [InlineData("No", "No", "cast-pill")]
    [InlineData("Present", "Present", "cast-pill")]
    public async Task OnlyAYesCastGetsTheFilledPillSoTheColorNeverClaimsMoreThanTheWordSays(string cast, string expectedText, string expectedClass)
    {
        var document = await RecordRenderedAsync([Vote('a', new DateOnly(2026, 4, 1)) with { Cast = cast }]);

        var pill = document.QuerySelector("[data-list-pane] .cast-pill")!;
        Assert.Equal((expectedText, expectedClass), (pill.TextContent, pill.ClassName));
    }

    [Fact]
    public async Task ARecordRowHasOneLinkOnItsTitleThatCoversTheWholeRowAndAnIconThatIsHiddenFromReaders()
    {
        var document = await RecordRenderedAsync([Vote('a', new DateOnly(2026, 4, 1))]);

        var row = document.QuerySelector("[data-list-pane] tbody tr")!;
        Assert.Single(row.QuerySelectorAll("a"));
        Assert.Equal("true", row.QuerySelector("svg.icon")!.GetAttribute("aria-hidden"));
    }

    [Fact]
    public async Task TheLedeNamesACaucusThatDiffersFromThePartyBecauseAnIndependentVotesWithThatCaucus()
    {
        var lede = await LedeAsync(Senate(new(2013, 1, 3), new(2027, 1, 3)) with { Party = "Independent", Caucus = "Democrat" });

        Assert.Equal("Independent senator for WA, caucuses with Democrats", lede);
    }

    private static ReleaseOfficialPage PageForSections()
    {
        var day = new DateOnly(2026, 1, 1);
        var votes = Enumerable.Range(0, 12).Select(index => Split((char)('a' + index), day.AddDays(index), index < 7 ? "Nay" : "Yea") with { IssueIds = ["housing"] });
        var bills = Enumerable.Range(1, 8).Select(index => Entry((char)('0' + index), PublishedRecordKind.Action, day.AddDays(index)) with { IssueIds = ["housing"], BillTitle = $"Bill {index}" });
        var statements = Enumerable.Range(0, 7).Select(index => Entry((char)('m' + index), PublishedRecordKind.Statement, day.AddDays(index)));
        return WithProfile(Current([.. bills, .. statements], [.. votes]));
    }

    private static string[] SeeAllTexts(AngleSharp.Dom.IDocument document) => [.. document.QuerySelectorAll(".answer-section .see-all a").Select(link => link.TextContent)];

    [Fact]
    public async Task EachSectionShowsAtMostFiveItemsAndEndsWithASeeAllLinkToTheRecordPageSoThePageStaysShort()
    {
        var document = await OfficialDocumentAsync(PageForSections());

        var sections = document.QuerySelectorAll(".answer-section").Where(section => section.Id != "issues").ToArray();
        Assert.Equal(["break", "bills", "votes", "statements"], sections.Select(section => section.Id));
        Assert.All(sections, section => Assert.Equal(5, section.QuerySelectorAll("tbody tr").Length));
        Assert.Equal(["See all 7 votes against their party", "See all 8 bills they introduced", "See the counts for all 1 issue", "See all 12 votes", "See all 7 statements"], SeeAllTexts(document));
        Assert.Equal(["../officials/mayor/record.html?tab=against-party", "../officials/mayor/record.html?tab=kind-action", "../officials/mayor/record.html?tab=issues",
            "../officials/mayor/record.html?tab=kind-vote", "../officials/mayor/record.html?tab=kind-statement"],
            document.QuerySelectorAll(".answer-section .see-all a").Select(link => link.GetAttribute("href")));
        var everyRecord = document.QuerySelectorAll(".see-all a").Last();
        Assert.Equal(("See all 27 records", "../officials/mayor/record.html"), (everyRecord.TextContent, everyRecord.GetAttribute("href")));
    }

    [Fact]
    public async Task ASectionListsTheNewestItemsFirstSoTheLatestVoteAgainstThePartyComesBeforeTheOlderOnes()
    {
        var document = await OfficialDocumentAsync(PageForSections());

        var ids = document.QuerySelectorAll("#break tbody a.cell-title").Select(link => link.GetAttribute("href")![^69..^5]).ToArray();
        Assert.Equal([new string('g', 64), new string('f', 64), new string('e', 64), new string('d', 64), new string('c', 64)], ids);
        Assert.Equal(["No (Nay)", "No (Nay)", "No (Nay)", "No (Nay)", "No (Nay)"], document.QuerySelectorAll("#break .cast-pill").Select(pill => pill.TextContent));
    }

    [Fact]
    public async Task ASectionWithNothingToShowSaysSoInPlainWordsInsteadOfDisappearingSoAReaderKnowsTheAbsenceIsReal()
    {
        var matched = await OfficialDocumentAsync(WithProfile(Current([], [Split('a', new DateOnly(2026, 4, 1), "Yea")])));
        var noVotes = await OfficialDocumentAsync(WithProfile(Current([], [])));

        string Line(AngleSharp.Dom.IDocument document, string id) => document.QuerySelector($"#{id} > p")!.TextContent;
        Assert.Equal(("No votes against most of their party.", "No bills on record.", "No statements on record."), (Line(matched, "break"), Line(matched, "bills"), Line(matched, "statements")));
        Assert.Equal("No votes on record.", Line(noVotes, "votes"));
        Assert.Null(noVotes.QuerySelector("#break"));
        Assert.Null(matched.QuerySelector("#break table"));
    }

    [Fact]
    public async Task AnOfficialWithNoProfileShowsOnlyTheStatementsSectionAndABillSectionOnlyWhenTheyHaveBills()
    {
        var plain = await OfficialDocumentAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []));
        var sponsor = await OfficialDocumentAsync(Current([Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 3, 1))], []));

        Assert.Equal(["statements"], plain.QuerySelectorAll(".answer-section").Select(section => section.Id));
        Assert.Equal(["bills", "statements"], sponsor.QuerySelectorAll(".answer-section").Select(section => section.Id));
    }

    [Fact]
    public async Task TheJumpRowLinksEachShownSectionByItsAnchorAndNoOtherSoNoLinkLeadsNowhere()
    {
        var document = await OfficialDocumentAsync(PageForSections());

        var jump = document.QuerySelector("nav.jump-links")!;
        var anchors = jump.QuerySelectorAll("a").Select(link => (link.GetAttribute("href"), link.TextContent)).ToArray();
        Assert.Equal([("#break", "Where they break from their party"), ("#bills", "Bills they introduced"), ("#issues", "Issues they work on"), ("#votes", "How they voted recently"), ("#statements", "What they said")], anchors);
        Assert.All(anchors, anchor => Assert.NotNull(document.QuerySelector($"section.answer-section{anchor.Item1}")));
        Assert.Equal("On this page", jump.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task AnIssueChipLinksTheRecordPageFilteredToItsIssueAndItsCountEqualsTheRowsThatFilterShows()
    {
        var page = PageForSections();
        var official = await OfficialDocumentAsync(page);
        var record = await RecordDocumentAsync(page);

        var chip = official.QuerySelector("#issues a.issue-chip")!;
        Assert.Equal("../officials/mayor/record.html?issue=housing", chip.GetAttribute("href"));
        var shown = record.QuerySelectorAll("[data-list-pane] tbody tr").Count(row => row.GetAttribute("data-filter-keys")!.Split(' ').Any(key => key.EndsWith(":housing", StringComparison.Ordinal)));
        Assert.Equal(20, shown);
        Assert.Equal(($"housing: 20 records", "20"), (chip.GetAttribute("aria-label"), chip.QuerySelector(".issue-chip-count")!.TextContent));
        Assert.Equal("Filtered to records on housing in the 119th Congress", record.QuerySelector("[data-issue-name='housing']")!.GetAttribute("data-issue-notice"));
    }

    [Fact]
    public async Task EverySeeAllLinkNamesATabOrIssueThatTheRecordPageHasSoTheLinkOpensTheListItPromises()
    {
        var page = PageForSections();
        var official = await OfficialDocumentAsync(page);
        var record = await RecordDocumentAsync(page);

        var tabs = record.QuerySelectorAll("[data-record-filters] [data-filter-value]").Select(tab => tab.GetAttribute("data-filter-value")).ToArray();
        var wanted = official.QuerySelectorAll(".answer-section .see-all a").Select(link => link.GetAttribute("href")!.Split("tab=")[1]).ToArray();
        Assert.All(wanted, tab => Assert.Contains(tab, tabs));
        Assert.Equal(7, record.QuerySelectorAll("[data-list-pane] tbody tr.against-party").Length);
        Assert.Equal(12, record.QuerySelectorAll("[data-list-pane] tbody tr.kind-vote").Length);
    }

    [Fact]
    public async Task TheRecordPageHoldsEveryRecordWithTheFullTabsAndThePagerWhileTheOfficialPageHoldsNoFullList()
    {
        var page = PageForSections();
        var official = await OfficialDocumentAsync(page);
        var record = await RecordDocumentAsync(page);

        Assert.Equal(27, record.QuerySelectorAll("[data-list-pane] tbody tr").Length);
        Assert.Equal("15", record.QuerySelector("[data-list-pane] table")!.GetAttribute("data-paged"));
        Assert.Equal(["All 27", "Statements 7", "Bills 8", "Votes 12", "Against their party 7", "Issues they work on"], record.QuerySelectorAll("[data-record-filters] [data-filter-value]").Select(TabText));
        Assert.Null(official.QuerySelector("[data-records]"));
        Assert.Null(official.QuerySelector("[data-list-pane]"));
        Assert.True(official.QuerySelectorAll("tbody tr").Length <= 4 * 5);
    }

    [Fact]
    public async Task TheRecordPageSitsTwoFoldersDeepSoItsAssetsAndLinksResolveFromTheReleaseRootAndItLinksBackToTheOfficialPage()
    {
        var record = await RecordDocumentAsync(PageForSections());

        Assert.Equal("All records of Mayor Example", record.QuerySelector("h1")!.TextContent);
        Assert.Equal("../../officials/mayor.html", record.QuerySelector(".page-header-meta a")!.GetAttribute("href"));
        Assert.Equal("Back to Mayor Example", record.QuerySelector(".page-header-meta a")!.TextContent);
        Assert.StartsWith("../../records/", record.QuerySelector("[data-list-pane] a.cell-title")!.GetAttribute("href"), StringComparison.Ordinal);
        Assert.NotNull(record.QuerySelector("link[href='../../assets/css/theme.css']"));
        Assert.Equal("All records of Mayor Example | Civic Lens", record.Title);
    }

    [Fact]
    public async Task TheIssuePanelShowsPlainCountsAndTheTitlesOfTheSponsoredBillsAndLeavesOutProceduralVotes()
    {
        PublishedRecordEntry[] entries = [.. Enumerable.Range(1, 3).Select(index => Entry((char)('0' + index), PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)) with { IssueIds = index < 3 ? ["housing", "health"] : ["housing"] }),
            Entry('a', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)) with { IssueIds = ["housing"], BillTitle = "Home Act" }];
        var page = Current(entries, [Vote('a', new DateOnly(2026, 4, 1)) with { IssueIds = ["housing"] }, Vote('b', new DateOnly(2026, 4, 2)) with { Kind = PublishedVoteKind.Cloture, IssueIds = ["housing"] },
            Vote('c', new DateOnly(2026, 4, 3)) with { IssueIds = ["health"], Cast = PublishedOfficialVote.NotVoting }]);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var panel = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='issues-title']")!;
        Assert.Equal(["Issue", "Their statements", "Bills they sponsored", "Votes they cast"], panel.QuerySelectorAll("th").Select(cell => cell.TextContent.Trim()));
        Assert.Equal(["health", "2 statements", "0 bills", "0 votes"], panel.QuerySelectorAll("tbody tr:nth-child(1) td").Select(cell => cell.TextContent.Trim()));
        var housing = panel.QuerySelectorAll("tbody tr:nth-child(2) td").ToArray();
        Assert.Equal(["housing", "3 statements", "1 bill", "1 vote"], new[] { housing[0], housing[1], housing[2].FirstChild!, housing[3] }.Select(cell => cell.TextContent.Trim()));
        Assert.Equal(["Home Act"], housing[2].QuerySelectorAll("li").Select(item => item.TextContent));
        Assert.Equal("../../" + PublicationProtocol.RecordPagePath(new string('a', 64)), housing[2].QuerySelector("a")!.GetAttribute("href"));
        Assert.DoesNotContain("%", panel.TextContent, StringComparison.Ordinal);
        Assert.Equal("../../method.html", panel.QuerySelector("p a")!.GetAttribute("href"));
        Assert.Contains("\"Votes they cast\" counts only substantive votes. A statement, bill, or vote on several issues counts once for each issue.", panel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWithNoRecordOnATrackedIssueGetsAPlainSentenceInPlaceOfTheIssueTable()
    {
        var page = Current([], [Vote('a', new DateOnly(2026, 4, 1))]);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var panel = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='issues-title']")!;
        Assert.Null(panel.QuerySelector("table"));
        Assert.Contains("No tracked issue", panel.TextContent, StringComparison.Ordinal);
        Assert.Contains("None of their statements, sponsored bills, or votes in this Congress is on a tracked issue.", panel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APageWithNoProfileHasNoProfilePanel()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("Issues they work on", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCurrentPageTabsLinkEachEarlierCongressFromItsOwnFolderAndSayThoseCongressesHoldVotesOnly()
    {
        var page = Current([], [Vote('3', new DateOnly(2026, 4, 1))]) with { Congresses = [119, 118, 117] };

        var tabs = await TabsOfAsync(page);

        Assert.Equal("Show the 119th Congress or an earlier Congress", tabs.GetAttribute("aria-label"));
        Assert.Equal(["All records, votes of the 119th Congress (2025 to 2027)", "118th Congress (votes only)", "117th Congress (votes only)"], tabs.QuerySelectorAll("a").Select(link => link.TextContent.Trim()));
        Assert.Equal(["../officials/mayor.html", "../officials/mayor/118.html", "../officials/mayor/117.html"], tabs.QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Equal(["All records, votes of the 119th Congress (2025 to 2027)"], tabs.QuerySelectorAll("[aria-current]").Select(link => link.TextContent.Trim()));
    }

    [Fact]
    public async Task AnEarlierCongressPageKeepsTheTabsWithALinkBackToTheCurrentPageAndMarksItsOwnTab()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 118, false, [119, 118, 117], [], [Vote('3', new DateOnly(2024, 4, 1)) with { Congress = 118 }], ListPublishedAtUtc);

        var tabs = await TabsOfAsync(page);

        Assert.Equal(["../../officials/mayor.html", "../../officials/mayor/118.html", "../../officials/mayor/117.html"], tabs.QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Equal(["118th Congress (votes only)"], tabs.QuerySelectorAll("[aria-current]").Select(link => link.TextContent.Trim()));
    }

    [Fact]
    public async Task TheFirstTabNamesBothScopesAndTheYearsOfItsCongressWhateverTheCongress()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 117, true, [117, 116], [], [Vote('3', new DateOnly(2022, 4, 1)) with { Congress = 117 }], ListPublishedAtUtc);

        var tabs = await TabsOfAsync(page);

        Assert.Equal("All records, votes of the 117th Congress (2021 to 2023)", tabs.QuerySelector("a")!.TextContent.Trim());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheTabLabelsDoNotDependOnThePublishDateSoACarriedPageStaysTrue(bool isCurrent)
    {
        async Task<string[]> LabelsAsync(DateTimeOffset publishedAtUtc)
        {
            var page = new ReleaseOfficialPage(Profile(), true, isCurrent ? 119 : 118, isCurrent, [119, 118], [], [Vote('3', new DateOnly(2026, 4, 1))], publishedAtUtc);
            return [.. (await TabsOfAsync(page)).QuerySelectorAll("a").Select(link => link.TextContent.Trim())];
        }

        var duringTheCongress = await LabelsAsync(new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var afterTheCongress = await LabelsAsync(new(2027, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(duringTheCongress, afterTheCongress);
        Assert.DoesNotContain(duringTheCongress, label => label.Contains("now", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOfficialWithOneCongressGetsNoTabsBecauseThereIsNothingToSwitchTo()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], [Vote('3', new DateOnly(2026, 4, 1))]), PublicationCatalogs.None, CancellationToken.None);

        Assert.Null(new HtmlParser().ParseDocument(html).QuerySelector("nav.segmented"));
    }

    private static async Task<AngleSharp.Dom.IElement> TabsOfAsync(ReleaseOfficialPage page)
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);
        return new HtmlParser().ParseDocument(html).QuerySelector("nav.segmented")!;
    }

    [Fact]
    public async Task AnEarlierCongressPageHoldsOnlyThatCongressVotesAndLinksBackAndResolvesItsAssetsFromTwoFoldersDeep()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 118, false, [], [], [Vote('3', new DateOnly(2024, 4, 1)) with { Congress = 118 }], ListPublishedAtUtc);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal(["../../records/" + new string('3', 64) + ".html"], document.QuerySelectorAll("tbody a.cell-title").Select(link => link.GetAttribute("href")));
        Assert.Contains("href=\"../../assets/css/theme.css\"", html, StringComparison.Ordinal);
        Assert.Null(document.QuerySelector("[aria-labelledby='panel-offices']"));
        Assert.Contains("the 118th Congress", document.QuerySelector("title")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCurrentPageDescriptionIsWholeSentencesWithTheOfficeAndTenureBecauseSearchResultsShowItAlone()
    {
        var profile = Profile() with
        {
            Terms = [new() { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 1, Party = "Democrat", Start = new(2021, 1, 3) }]
        };
        var page = new ReleaseOfficialPage(profile, true, 119, true, [119], [], [], ListPublishedAtUtc);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var description = new HtmlParser().ParseDocument(html).QuerySelector("meta[name='description']")!.GetAttribute("content")!;
        Assert.Equal("Mayor Example. Democratic representative for WA, district 1. In the House since January 2021.", description);
    }

    [Theory]
    [InlineData(118, "Mayor Example, Representative for WA, district 1. Votes in the 118th Congress")]
    [InlineData(119, "Mayor Example. Votes in the 119th Congress")]
    [InlineData(110, "Mayor Example. Votes in the 110th Congress")]
    public async Task AnEarlierCongressDescriptionNamesTheOfficeHeldInThatCongressAndNoneWhenThereWasNone(int congress, string expectedStart)
    {
        var profile = Profile() with
        {
            Terms = [new() { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 1, Party = "Democrat", Start = new(2021, 1, 3), End = new(2025, 1, 3) }]
        };
        var page = new ReleaseOfficialPage(profile, true, congress, false, [], [], [], ListPublishedAtUtc);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var description = new HtmlParser().ParseDocument(html).QuerySelector("meta[name='description']")!.GetAttribute("content")!;
        Assert.StartsWith(expectedStart, description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWithNoTermStatesThatTheRostersAndTheConfigurationListNoOffice()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []),
            PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains("The rosters and the configuration list no dated office for this official.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWhoLeftTheConfigurationSaysThatCivicLensNoLongerTracksThem()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []) with { IsTracked = false },
            PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains("Civic Lens no longer tracks this official", html, StringComparison.Ordinal);
        Assert.DoesNotContain("The rosters and the configuration list no dated office", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheIssueColumnShowsTheConfiguredNameLinkedToTheIssuePageAndAnUnconfiguredIdAsPlainText()
    {
        PublishedRecordEntry[] entries = [Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)) with { IssueIds = ["housing", "mystery"] }];
        var page = Current(entries, []);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };
        var catalog = new PublicationCatalog(new Dictionary<string, string>(), new IssueConfiguration
        {
            PolicyAreas = [new() { Id = "land-use", Name = "Land use" }],
            Issues = [new() { Id = "housing", Name = "Housing costs", Definition = "d", PolicyAreaIds = ["land-use"] }]
        }, IssueIconConfiguration.None);

        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, catalog, CancellationToken.None);

        var cells = new HtmlParser().ParseDocument(html)
            .QuerySelectorAll("[aria-labelledby='issues-title'] tbody tr td:first-child").ToList();
        Assert.Equal(["Housing costs", "mystery"], cells.Select(cell => cell.TextContent.Trim()));
        Assert.Equal("../../" + PublicationProtocol.IssuePagePath("housing", 1), cells[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(cells[1].QuerySelector("a"));
    }

    [Fact]
    public async Task TheOfficialListShowsOneCardForEveryOfficialWithTheLinkOfficeAndPartyAndAFullSentenceCount()
    {
        var senator = Profile("warren", "Elizabeth Warren", Term("sen", "MA", "Democratic"));
        var quiet = Profile("quiet", "Quiet Member", Term("rep", "TX", "Republican") with { District = 3 });

        var cards = (await ListAsync(new OfficialListing(senator, 12), new OfficialListing(quiet, 0), new OfficialListing(Profile("one", "One Record"), 1)))
            .QuerySelectorAll("ul.officials > li").ToList();

        Assert.Equal(3, cards.Count);
        Assert.Equal("officials/warren.html", cards[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal(["Senator for MA", "Party: Democratic", "12 records other than votes"], cards[0].QuerySelectorAll(".cell-sub").Select(line => line.TextContent));
        Assert.Contains("0 records other than votes", cards[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Representative for TX, district 3", cards[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("1 record other than votes", cards[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExecutiveCardShowsTheOfficeWithNoStateBecauseAnExecutiveHasNone()
    {
        var executive = Profile("pres", "Pat President", Term("prez", null, "Independent"));

        var card = (await ListAsync(new OfficialListing(executive, 4))).QuerySelector("ul.officials > li")!;

        Assert.Equal("President", card.QuerySelector(".cell-sub")!.TextContent);
        Assert.Null(card.GetAttribute("data-state"));
        Assert.Equal("executive", card.GetAttribute("data-office"));
    }

    [Fact]
    public async Task TheCardsCarryTheDataTheBrowserFiltersOnAndTheTabsSayWhoAndHowMany()
    {
        var page = await ListAsync(
            new OfficialListing(Profile("a", "Ann Able", Term("sen", "MA", "Democratic")), 1),
            new OfficialListing(Profile("b", "Bob Baker", Term("sen", "TX", "Republican")), 1),
            new OfficialListing(Profile("c", "Cy Cole", Term("rep", "MA", "Democratic")), 1));

        var cards = page.QuerySelectorAll("ul.officials > li").ToList();
        Assert.Equal([("Ann Able", "senator", "MA"), ("Bob Baker", "senator", "TX"), ("Cy Cole", "representative", "MA")],
            cards.Select(card => (card.GetAttribute("data-name")!, card.GetAttribute("data-office")!, card.GetAttribute("data-state")!)));
        Assert.Equal(["All officials (3)", "Senators (2)", "Representatives (1)"],
            page.QuerySelectorAll("[data-officials-filters] button").Select(button => button.TextContent.Trim()));
        Assert.Equal(["All states", "MA", "TX"], page.QuerySelectorAll("[data-officials-state] option").Select(option => option.TextContent));
        Assert.Equal(["true", "false", "false"], page.QuerySelectorAll("[data-officials-filters] button").Select(button => button.GetAttribute("aria-pressed")));
    }

    [Fact]
    public async Task AJudicialOfficeIsInTheJusticesTabAfterExecutivesAndAnExecutiveOfficeIsInExecutivesNotOtherOfficials()
    {
        var page = await ListAsync(
            new OfficialListing(Profile("a", "Ann Able", Term("sen", "MA", "Democratic")), 1),
            new OfficialListing(Profile("b", "Bob Baker", new PublishedOfficeTerm { Office = "Chief Justice", Kind = PublishedOfficeKind.Judicial, Start = new DateOnly(2005, 9, 29) }), 1),
            new OfficialListing(Profile("c", "Cy Cole", new PublishedOfficeTerm { Office = "Secretary of State", Kind = PublishedOfficeKind.Executive, Start = new DateOnly(2025, 1, 21) }), 1));

        Assert.Equal(["All officials (3)", "Senators (1)", "Executives (1)", "Justices (1)"],
            page.QuerySelectorAll("[data-officials-filters] button").Select(button => button.TextContent.Trim()));
        Assert.Equal(["senator", "judicial", "executive"], page.QuerySelectorAll("ul.officials > li").Select(card => card.GetAttribute("data-office")));
    }

    [Fact]
    public async Task TheFilterControlsAreHiddenInTheStaticPageSoAReaderWithoutScriptSeesEveryCardAndNoDeadControl()
    {
        var page = await ListAsync(new OfficialListing(Profile("a", "Ann Able", Term("sen", "MA", "Democratic")), 1));

        Assert.True(page.QuerySelector("[data-officials-filters]")!.HasAttribute("hidden"));
        Assert.False(page.QuerySelector("ul.officials > li")!.HasAttribute("hidden"));
        Assert.Equal("status", page.QuerySelector("[data-officials-status]")!.GetAttribute("role"));
    }

    [Fact]
    public async Task TheLatestTermByStartDecidesTheOfficeAndKindSoASenatorWhoWasARepresentativeIsASenator()
    {
        var profile = Profile("a", "Ann Able",
            Term("sen", "MA", "Democratic") with { Start = new DateOnly(2021, 1, 3) },
            Term("rep", "MA", "Democratic") with { Start = new DateOnly(2015, 1, 3) });

        var card = (await ListAsync(new OfficialListing(profile, 1))).QuerySelector("ul.officials > li")!;

        Assert.Contains("Senator for MA", card.TextContent, StringComparison.Ordinal);
        Assert.Equal("senator", card.GetAttribute("data-office"));
    }

    [Fact]
    public async Task AnEndedLatestTermReadsFormerButStillGroupsUnderItsOfficeSoTheReaderKnowsTheOfficialLeft()
    {
        var term = Term("rep", "MA", "Democratic") with { District = 4, End = new DateOnly(2025, 1, 3) };

        var card = (await ListAsync(new OfficialListing(Profile("a", "Ann Able", term), 1))).QuerySelector("ul.officials > li")!;

        Assert.Equal("Former Representative for MA, district 4", card.QuerySelector(".cell-sub")!.TextContent);
        Assert.Equal("representative", card.GetAttribute("data-office"));
    }

    [Fact]
    public async Task ATermEndsByTheReleaseDateNotTheRenderClockSoAListPublishedBeforeTheEndShowsTheOfficeAsCurrent()
    {
        var term = Term("rep", "MA", "Democratic") with { District = 4, Start = new DateOnly(2023, 1, 3), End = new DateOnly(2025, 1, 3) };

        var page = await ListAsync(new DateTimeOffset(2025, 1, 2, 23, 0, 0, TimeSpan.Zero), new OfficialListing(Profile("a", "Ann Able", term), 1));

        Assert.Equal("Representative for MA, district 4", page.QuerySelector("ul.officials > li .cell-sub")!.TextContent);
    }

    [Fact]
    public async Task ACaucusThatDiffersFromThePartyIsNamedSoAnIndependentShowsWhoseCaucusTheyJoin()
    {
        var independent = Term("sen", "VT", "Independent") with { Caucus = "Democrat" };
        var same = Term("sen", "TX", "Republican") with { Caucus = "Republican" };

        var cards = (await ListAsync(new OfficialListing(Profile("a", "Ann Able", independent), 1), new OfficialListing(Profile("b", "Bob Baker", same), 1)))
            .QuerySelectorAll("ul.officials > li").ToList();

        Assert.Equal("Party: Independent, caucuses with Democrats", cards[0].QuerySelectorAll(".cell-sub")[1].TextContent);
        Assert.Equal("Party: Republican", cards[1].QuerySelectorAll(".cell-sub")[1].TextContent);
    }

    [Fact]
    public async Task AnOfficialWhoseTermsAllStartAfterTheReleaseDateIsInTheOtherOfficialsTabBecauseNoTermIsTheLatest()
    {
        var page = await ListAsync(new OfficialListing(Profile("a", "Ann Able", Term("sen", "MA", "Democratic") with { Start = new DateOnly(2027, 1, 3) }), 1));

        Assert.Equal("other", page.QuerySelector("ul.officials > li")!.GetAttribute("data-office"));
    }

    [Fact]
    public async Task AnOfficeThatStartsAfterTheReleaseDateIsNotInTheOfficesTableSoItIsNeverShownAsPresent()
    {
        var rows = await OfficeRowsAsync(Seat(new(2025, 1, 3), new(2027, 1, 3)), new PublishedOfficeTerm { Office = "Mayor", Kind = PublishedOfficeKind.Other, Start = new(2027, 1, 20) });

        Assert.Equal(["3 Jan 2025 to 3 Jan 2027"], rows);
    }

    [Fact]
    public async Task AnOfficialWithNoTermIsInTheOtherOfficialsTabAndAllOfficialsOnly()
    {
        var page = await ListAsync(new OfficialListing(Profile("a", "Ann Able", Term("sen", "MA", "Democratic")), 1), new OfficialListing(Profile("b", "No Term"), 1));

        Assert.Equal("other", page.QuerySelectorAll("ul.officials > li")[1].GetAttribute("data-office"));
        Assert.Equal(["All officials (2)", "Senators (1)", "Other officials (1)"],
            page.QuerySelectorAll("[data-officials-filters] button").Select(button => button.TextContent.Trim()));
    }

    [Fact]
    public async Task TheCardListKeepsItsListRoleBecauseListStyleNoneDropsItInSafari()
    {
        var page = await ListAsync(new OfficialListing(Profile("a", "Ann Able"), 1));

        Assert.Equal("list", page.QuerySelector("ul.officials")!.GetAttribute("role"));
    }

    [Fact]
    public async Task CardPortraitsLoadLazilyAndACardWithNoPhotoShowsThePlaceholder()
    {
        var page = await ListAsync(new OfficialListing(WithPhoto("a", "Ann Able", "Public domain."), 1), new OfficialListing(Profile("b", "Bob Baker"), 1));

        var cards = page.QuerySelectorAll("ul.officials > li").ToList();
        var image = cards[0].QuerySelector("img")!;
        Assert.Equal(("lazy", "225", "275"), (image.GetAttribute("loading"), image.GetAttribute("width"), image.GetAttribute("height")));
        Assert.Null(cards[1].QuerySelector("img"));
        Assert.NotNull(cards[1].QuerySelector("svg.portrait-placeholder"));
        Assert.Null(cards[1].QuerySelector("figcaption"));
    }

    [Fact]
    public async Task TheCreditShowsOnceBelowTheListWhenEveryPhotoHasTheSameCredit()
    {
        var page = await ListAsync(new OfficialListing(WithPhoto("a", "Ann Able", "Public domain."), 1), new OfficialListing(WithPhoto("b", "Bob Baker", "Public domain."), 1));

        Assert.Empty(page.QuerySelectorAll("ul.officials figcaption"));
        Assert.Single(page.QuerySelectorAll(".caveat"));
        Assert.Contains("Public domain.", page.QuerySelector(".caveat")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCreditShowsOnEachCardWhenCreditsDifferSoNoLicenseTextIsHidden()
    {
        var page = await ListAsync(new OfficialListing(WithPhoto("a", "Ann Able", "Public domain."), 1), new OfficialListing(WithPhoto("b", "Bob Baker", "Used with permission."), 1));

        Assert.Equal(["Public domain.", "Used with permission."], page.QuerySelectorAll("ul.officials figcaption").Select(caption => caption.TextContent));
        Assert.Empty(page.QuerySelectorAll(".caveat"));
    }

    [Fact]
    public async Task TheProfilePortraitStaysEagerBecauseItIsTheFirstThingOnThePage()
    {
        var page = Current([], []) with { Profile = WithPhoto("mayor", "Mayor Example", "Public domain.") };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        Assert.Equal("eager", new HtmlParser().ParseDocument(html).QuerySelector("figure.portrait img")!.GetAttribute("loading"));
    }

    [Fact]
    public async Task TheLinkedPairsPanelComesAfterTheRecordListAndListsEachPairWithTheLinkWordsTheTitleAndTheCast()
    {
        var page = Current([], [Split('a', new DateOnly(2026, 4, 1), "Nay")]);
        page = page with
        {
            TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes),
            Pairs = [Pair('2', 'a', PublishedRelationshipType.StatementAboutVote, PublishedRecordKind.Vote, new DateOnly(2026, 4, 5), "Nay"),
                Pair('1', 'b', PublishedRelationshipType.StatesPositionOn, PublishedRecordKind.Action, new DateOnly(2026, 3, 1), null)]
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var panels = document.QuerySelectorAll("section[aria-labelledby]").Select(panel => panel.GetAttribute("aria-labelledby")).ToArray();
        Assert.True(Array.IndexOf(panels, "record-title") < Array.IndexOf(panels, "panel-what-they-said-and-what-they-did"));
        Assert.True(Array.IndexOf(panels, "issues-title") < Array.IndexOf(panels, "panel-what-they-said-and-what-they-did"));
        var panel = document.QuerySelector("[aria-labelledby='panel-what-they-said-and-what-they-did']")!;
        Assert.Equal(["Statement date", "What they said", "Link", "What they did", "Their vote"], panel.QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim()));
        var rows = panel.QuerySelectorAll("tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToArray()).ToArray();
        Assert.Equal(["5 Apr 2026", "Statement 2", "Statement about a vote", "Official title a", "No (Nay)"], rows[0]);
        Assert.Equal(["1 Mar 2026", "Statement 1", "States position on", "Official title b", "Not a vote"], rows[1]);
        Assert.Equal([$"../records/{new string('2', 64)}.html", $"../records/{new string('a', 64)}.html"],
            panel.QuerySelectorAll("tbody tr")[0].QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Contains("A reviewer approved each link. A link does not mean the vote or bill matches the statement.", panel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryWholeTableOnTheOfficialPageSortsInTheBrowserByTheValueBehindACellNeverByItsDisplayedText()
    {
        var page = Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)) with { IssueIds = ["housing"] }], [Split('a', new DateOnly(2026, 4, 1), "Nay") with { IssueIds = ["housing"] }]);
        page = page with
        {
            Profile = WithOffice(),
            TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes),
            Pairs = [Pair('1', 'b', PublishedRelationshipType.StatesPositionOn, PublishedRecordKind.Action, new DateOnly(2026, 3, 1), null),
                Pair('2', 'b', PublishedRelationshipType.StatesPositionOn, PublishedRecordKind.Action, null, null)]
        };
        var document = await OfficialDocumentAsync(page);
        var record = await RecordDocumentAsync(page);

        string[] Kinds(AngleSharp.Dom.IDocument from, string panel) => [.. from.QuerySelectorAll($"[aria-labelledby='{panel}'] thead th").Select(header => header.GetAttribute("data-sort") ?? "none")];
        string[] Values(AngleSharp.Dom.IDocument from, string panel, string label) => [.. from.QuerySelectorAll($"[aria-labelledby='{panel}'] tbody td[data-label='{label}']").Select(cell => cell.GetAttribute("data-sort-value") ?? "none")];
        Assert.Equal(["text", "number", "number", "number"], Kinds(record, "issues-title"));
        Assert.Equal(["1"], Values(record, "issues-title", "Their statements"));
        Assert.Equal(["0"], Values(record, "issues-title", "Bills they sponsored"));
        Assert.Equal(["1"], Values(record, "issues-title", "Votes they cast"));
        Assert.Equal(["date", "text", "text", "text", "text"], Kinds(document, "panel-what-they-said-and-what-they-did"));
        Assert.Equal(["2026-03-01", ""], Values(document, "panel-what-they-said-and-what-they-did", "Statement date"));
        Assert.Equal(["none", "none"], record.QuerySelectorAll("[data-list-pane] thead th").Select(header => header.GetAttribute("data-sort") ?? "none"));
        Assert.Equal(["text", "text", "date", "none"], Kinds(document, "panel-offices"));
        Assert.Equal(["1997-01-07"], Values(document, "panel-offices", "From"));
    }

    [Fact]
    public async Task EachSortableTableNamesItsDefaultOrderInItsCaptionSoTheOrderWithoutScriptsIsStated()
    {
        var page = Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)) with { IssueIds = ["housing"] }], [Split('a', new DateOnly(2026, 4, 1), "Nay")]);
        page = page with
        {
            Profile = WithOffice(),
            TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes),
            Pairs = [Pair('1', 'b', PublishedRelationshipType.StatesPositionOn, PublishedRecordKind.Action, new DateOnly(2026, 3, 1), null)]
        };
        var tables = (await OfficialDocumentAsync(page)).QuerySelectorAll("table").Concat((await RecordDocumentAsync(page)).QuerySelectorAll("table")).ToArray();

        Assert.Equal(["Newest first", "Newest first", "Newest first", "Newest first", "Default order", "Newest first", "Default order"], tables.Select(table => table.GetAttribute("data-default-order")));
        Assert.All(tables, table =>
            Assert.EndsWith($". {table.GetAttribute("data-default-order")}.", table.QuerySelector("caption")!.TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AVoteLinkWithNoCastRowOfTheOfficialSaysSoInsteadOfLeavingTheCellEmpty()
    {
        var page = Current([], []);
        page = page with
        {
            TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes),
            Pairs = [Pair('1', 'a', PublishedRelationshipType.StatementAboutVote, PublishedRecordKind.Vote, new DateOnly(2026, 4, 5), null)]
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var cells = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='panel-what-they-said-and-what-they-did'] tbody td");
        Assert.Equal("No vote on record", cells[^1].TextContent.Trim());
    }

    [Fact]
    public async Task AnUndatedStatementShowsNoDateInsteadOfAnInventedDay()
    {
        var page = Current([], []);
        page = page with
        {
            TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes),
            Pairs = [Pair('1', 'b', PublishedRelationshipType.StatesPositionOn, PublishedRecordKind.Action, null, null)]
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var cells = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='panel-what-they-said-and-what-they-did'] tbody td");
        Assert.Equal("No date", cells[0].TextContent.Trim());
    }

    [Fact]
    public async Task AnOfficialWithNoPairKeepsThePanelWithOneLineThatSaysThereIsNoLinkYet()
    {
        var page = Current([], []);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var panel = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-what-they-said-and-what-they-did']")!;
        Assert.Contains("No reviewed links between their statements and their bills or votes yet.", panel.TextContent, StringComparison.Ordinal);
        Assert.Null(panel.QuerySelector("table"));
    }

    [Fact]
    public async Task AnEarlierCongressPageHasNoLinkedPairsPanel()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 118, false, [], [], [Vote('3', new DateOnly(2024, 4, 1)) with { Congress = 118 }], ListPublishedAtUtc);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("What they said and what they did", html, StringComparison.Ordinal);
    }

    private static PublishedOfficialProfile WithOffice() =>
        Profile("mayor", "Mayor Example", new PublishedOfficeTerm { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 9, Party = "Democrat", Start = new(1997, 1, 7) });

    private static LinkedPair Pair(char statement, char target, PublishedRelationshipType type, PublishedRecordKind kind, DateOnly? day, string? cast) =>
        new(day, new string(statement, 64), $"Statement {statement}", type, kind, new string(target, 64), $"Official title {target}", cast);

    [Fact]
    public async Task EachCountButtonNamesTheRowsItFiltersAndTheNumberShownEqualsTheCountClicked()
    {
        var document = await RecordRenderedWithCountsAsync();

        var counts = document.QuerySelectorAll("[aria-labelledby='issues-title'] [data-count-filter]").ToArray();
        Assert.Equal(["statement:health", "statement:housing", "bill:housing", "vote:housing"], counts.Select(count => count.GetAttribute("data-count-filter")));
        foreach (var count in counts)
        {
            var rows = document.QuerySelectorAll("[data-list-pane] tbody tr").Where(row => row.GetAttribute("data-filter-keys")!.Split(' ').Contains(count.GetAttribute("data-count-filter"))).ToArray();
            Assert.Equal(int.Parse(count.TextContent.Split(' ')[0]), rows.Length);
            Assert.StartsWith(count.TextContent + ":", count.GetAttribute("data-filter-label"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OnlyARowInTheCurrentCongressCarriesAStatementOrBillMarkerAndOnlyASubstantiveCastVoteCarriesAVoteMarker()
    {
        var document = await RecordRenderedWithCountsAsync();

        var marked = document.QuerySelectorAll("[data-list-pane] tbody tr").ToDictionary(row => row.QuerySelector("a.cell-title")!.TextContent, row => row.GetAttribute("data-filter-keys"));
        Assert.Equal("statement:health statement:housing", marked["Entry 1"]);
        Assert.Equal("", marked["Entry 5"]);
        Assert.Equal("", marked["Entry 6"]);
        Assert.Equal("bill:housing", marked["Entry d"]);
        Assert.Equal("vote:housing", marked["Vote a"]);
        Assert.Equal("", marked["Vote b"]);
        Assert.Equal("", marked["Vote c"]);
    }

    [Fact]
    public async Task TheServerHtmlHasPlainCountsWithAZeroNeverAButtonAndEveryRowVisible()
    {
        var document = await RecordRenderedWithCountsAsync();

        var health = document.QuerySelectorAll("[aria-labelledby='issues-title'] tbody tr:nth-child(1) td").Select(cell => cell.TextContent.Trim()).ToArray();
        Assert.Equal(["health", "1 statement", "0 bills", "0 votes"], health);
        Assert.Equal(4, document.QuerySelectorAll("[data-count-filter]").Length);
        Assert.Empty(document.QuerySelectorAll("[data-count-filter] button"));
        Assert.Empty(document.QuerySelectorAll("[data-records] tbody tr[hidden]"));
        Assert.Equal(9, document.QuerySelectorAll("[data-list-pane] tbody tr").Length);
    }

    [Fact]
    public async Task ABillTitleIsClampedToTwoLinesAndItsLinkKeepsTheFullTitle()
    {
        var document = await RecordRenderedWithCountsAsync();

        var link = document.QuerySelector("li.clamp-2 a")!;
        Assert.Equal("Home Act", link.GetAttribute("title"));
        Assert.Equal("Home Act", link.TextContent);
    }

    [Fact]
    public async Task TheFilterNoticeStatesWhatIsShownInPlainWordsAndOffersToShowAllRecords()
    {
        var document = await RecordRenderedWithCountsAsync();

        var notice = document.QuerySelector("[data-records] [data-filter-notice][hidden]")!;
        Assert.Equal("Show all records", notice.QuerySelector("button[data-filter-clear]")!.TextContent);
        var counts = document.QuerySelectorAll("[data-count-filter]").ToArray();
        Assert.Equal(["Filtered to their statements on health in the 119th Congress",
            "Filtered to their statements on housing in the 119th Congress",
            "Filtered to bills they sponsored on housing in the 119th Congress",
            "Filtered to their substantive votes on housing in the 119th Congress"], counts.Select(count => count.GetAttribute("data-filter-shows")));
        Assert.Equal("1 bill: show bills they sponsored on housing", counts[2].GetAttribute("data-filter-label"));
        Assert.Equal("", document.QuerySelector("[data-records] [data-filter-status][role='status']")!.TextContent);
        Assert.False(document.QuerySelector("[data-filter-status]")!.HasAttribute("hidden"));
    }

    [Fact]
    public async Task AnEmptyListWithNoProfileHasNoRecordsFilterMarkup()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Null(document.QuerySelector("[data-records]"));
        Assert.Null(document.QuerySelector("[data-filter-notice]"));
        Assert.Null(document.QuerySelector("tbody tr[data-filter-keys]"));
    }

    [Fact]
    public async Task APageWithNoProfileStillFiltersByKindButHasNoIssueNotice()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(
            Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], [Vote('a', new DateOnly(2026, 4, 1))]), PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal(["All 2", "Statements 1", "Votes 1"], document.QuerySelectorAll("[data-records] [data-record-filters] [data-filter-value]").Select(TabText));
        Assert.NotNull(document.QuerySelector("[data-records] [data-filter-status][role='status']"));
        Assert.Null(document.QuerySelector("[data-filter-notice]"));
    }

    private static string TabText(AngleSharp.Dom.IElement tab) => tab.QuerySelector(".segmented-count") is { } count ? $"{tab.FirstChild!.TextContent.Trim()} {count.TextContent}" : tab.TextContent.Trim();

    private static Task<AngleSharp.Dom.IDocument> RenderedWithCountsAsync() => OfficialDocumentAsync(PageWithCounts());

    private static Task<AngleSharp.Dom.IDocument> RecordRenderedWithCountsAsync() => RecordDocumentAsync(PageWithCounts());

    private static ReleaseOfficialPage PageWithCounts()
    {
        PublishedRecordEntry[] entries =
        [.. Enumerable.Range(1, 3).Select(index => Entry((char)('0' + index), PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)) with { IssueIds = index == 1 ? ["housing", "health"] : ["housing"] }),
            Entry('5', PublishedRecordKind.Statement, new DateOnly(2024, 3, 1)) with { IssueIds = ["housing"] },
            Entry('6', PublishedRecordKind.Action, new DateOnly(2024, 3, 1)) with { IssueIds = ["housing"] },
            Entry('d', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)) with { IssueIds = ["housing"], BillTitle = "Home Act" }];
        var page = Current(entries, [Vote('a', new DateOnly(2026, 4, 1)) with { IssueIds = ["housing"] },
            Vote('b', new DateOnly(2026, 4, 2)) with { Kind = PublishedVoteKind.Cloture, IssueIds = ["housing"] },
            Vote('c', new DateOnly(2026, 4, 3)) with { IssueIds = ["housing"], Cast = PublishedOfficialVote.NotVoting }]);
        return WithProfile(page);
    }

    private static PublishedOfficialProfile Profile() => new() { Id = "mayor", Name = "Mayor Example", Terms = [] };

    private static PublishedOfficialProfile Profile(string id, string name, params PublishedOfficeTerm[] terms) => new() { Id = id, Name = name, Terms = terms };

    private static PublishedOfficeTerm Term(string office, string? state, string party) =>
        new() { Office = office, Kind = KindOf(office), State = state, Party = party, Start = new DateOnly(2025, 1, 3) };

    private static PublishedOfficeKind KindOf(string office) => office switch
    {
        "sen" => PublishedOfficeKind.Senator,
        "rep" => PublishedOfficeKind.Representative,
        "prez" => PublishedOfficeKind.Executive,
        _ => throw new ArgumentOutOfRangeException(nameof(office))
    };

    private static PublishedOfficialProfile WithPhoto(string id, string name, string credit) => Profile(id, name) with
    {
        Photo = new PublishedPhoto
        {
            Path = PublicationProtocol.PhotoPath(new string('c', 64)),
            Credit = credit,
            SourceUrl = "https://example.gov/a.jpg",
            Width = 225,
            Height = 275
        }
    };

    private static readonly DateTimeOffset ListPublishedAtUtc = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static Task<AngleSharp.Dom.IDocument> ListAsync(params OfficialListing[] officials) => ListAsync(ListPublishedAtUtc, officials);

    private static async Task<AngleSharp.Dom.IDocument> ListAsync(DateTimeOffset publishedAtUtc, params OfficialListing[] officials) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialsAsync(officials, publishedAtUtc, CancellationToken.None));

    private static PublishedOfficeTerm Seat(DateOnly start, DateOnly? end) => new()
    {
        Office = "rep",
        Kind = PublishedOfficeKind.Representative,
        State = "WA",
        District = 9,
        Party = "Democrat",
        Start = start,
        End = end
    };

    private static PublishedOfficeTerm Senate(DateOnly start, DateOnly? end) => new()
    {
        Office = "sen",
        Kind = PublishedOfficeKind.Senator,
        State = "WA",
        Party = "Democrat",
        Start = start,
        End = end
    };

    private static async Task<AngleSharp.Dom.IDocument> OfficialAsync(params PublishedOfficeTerm[] terms) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialAsync(
            Current([], []) with { Profile = Profile("rep", "Rita Rep", terms) }, PublicationCatalogs.None, CancellationToken.None));

    private static async Task<string?> TenureAsync(params PublishedOfficeTerm[] terms) => (await OfficialAsync(terms)).QuerySelector(".page-header-meta span")?.TextContent;

    private static async Task<string?> LedeAsync(params PublishedOfficeTerm[] terms) => (await OfficialAsync(terms)).QuerySelector(".page-header-lede")?.TextContent;

    private static async Task<string[]> OfficeRowsAsync(params PublishedOfficeTerm[] terms) =>
        [.. (await OfficialAsync(terms)).QuerySelectorAll("[aria-labelledby='panel-offices'] tbody tr")
            .Select(row => $"{row.QuerySelector("td[data-label='From']")!.TextContent} to {row.QuerySelector("td[data-label='To']")!.TextContent}")];

    private static async Task<AngleSharp.Dom.IElement> RecordPanelAsync(ReleaseOfficialPage page, string panelId) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None))
            .QuerySelector($"[aria-labelledby='{panelId}']")!;

    private static async Task<AngleSharp.Dom.IElement> RecordPanelTableAsync(ReleaseOfficialPage page, string panelId) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialRecordAsync(page, PublicationCatalogs.None, CancellationToken.None))
            .QuerySelector($"[aria-labelledby='{panelId}'] table")!;

    private static async Task<AngleSharp.Dom.IElement> PanelAsync(ReleaseOfficialPage page, string panelId) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None))
            .QuerySelector($"[aria-labelledby='{panelId}']")!;

    private static async Task<AngleSharp.Dom.IElement> PanelTableAsync(ReleaseOfficialPage page, string panelId) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None))
            .QuerySelector($"[aria-labelledby='{panelId}'] table")!;

    private static ReleaseOfficialPage Current(PublishedRecordEntry[] entries, PublishedOfficialVote[] votes) =>
        new(Profile(), true, 119, true, [119], [.. entries], [.. votes], ListPublishedAtUtc);

    private static PublishedRecordEntry Entry(char id, PublishedRecordKind kind, DateOnly date) => new()
    {
        IssueIds = [],
        PolicyAreaIds = [],
        Kind = kind,
        RecordId = new string(id, 64),
        RevisionNumber = 1,
        Headline = $"Entry {id}",
        FirstPublishedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        EventDate = date,
        Officials = [new() { Id = "mayor", Name = "Mayor Example" }]
    };

    private static PublishedOfficialVote Vote(char id, DateOnly date) => new()
    {
        RecordId = new string(id, 64),
        VotedOn = date,
        Chamber = PublishedChamber.House,
        Congress = 119,
        Session = 1,
        RollCallNumber = 100,
        BillType = "hr",
        BillNumber = 7,
        Cast = "Yea",
        Result = "Passed",
        Totals = new() { Yea = 218, Nay = 210, Present = 1, NotVoting = 5 },
        SplitVote = false,
        Party = "Republican",
        Headline = $"Vote {id}",
        Kind = PublishedVoteKind.Passage,
        IssueIds = []
    };
}
