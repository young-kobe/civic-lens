using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Core.Votes;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishVoteTests
{
    [Fact]
    public async Task AVoteEntryNamesNoOfficialAndDatesTheVoteByItsDay()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var entry = Assert.Single(ReadManifest(scenario, summary.DirectoryName).Records);
        Assert.Equal(PublishedRecordKind.Vote, entry.Kind);
        Assert.Empty(entry.Officials);
        Assert.Empty(entry.PolicyAreaIds);
        Assert.Equal(["housing"], entry.IssueIds);
        Assert.Equal("api.congress.gov", entry.SourceHost);
        Assert.Equal(new DateOnly(2025, 4, 10), entry.EventDate);
    }

    [Fact]
    public async Task TheRecordEmbedsEachCitedApiPageOnceAndNamesTheMemberByConfiguredName()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Contains(PublicationProtocol.RecordPagePath(vote), files.Keys);
        var record = JsonSerializer.Deserialize<PublishedVote>(files[PublicationProtocol.RecordDataPath(vote)], PublicationProtocol.JsonOptions)!;
        Assert.Equal([PublicationScenario.VoteDetail, PublicationScenario.VoteMembers], record.Sources.Select(source => source.Text));
        var member = Assert.Single(record.Members);
        Assert.Equal(("Mayor Example", "Yea"), (member.Official.Name, member.Cast));
        Assert.Equal(PublicationScenario.ApprovedAt, record.ApprovedAtUtc);
    }

    [Fact]
    public async Task CommitBindsTheVoteToItsKindAndReviewStateSoTheStoreChecksTheVoteTable()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var commit = scenario.Publications.LastCommit!;
        Assert.Equal(new PublishedRevisionBinding(PublishedRecordKind.Vote, vote, 1), Assert.Single(commit.Records));
        Assert.Equal(new ReviewStateExpectation(PublishedRecordKind.Vote, vote, 1, 1), Assert.Single(commit.AddedRecords));
    }

    [Fact]
    public async Task AVoteWhoseRevisionNobodyApprovedIsRefusedBecauseAModelRevisionNeverPublishesAlone()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', decisions: []);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote)));

        Assert.Equal($"Vote {vote} is not approved or has unresolved concerns.", error.Message);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task AVoteWithAnOpenConcernIsRefusedAndIsNotListed()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', decisions: [PublicationScenario.Decision(new string('d', 64), 1, ReviewDecisionKind.RequestChanges)]);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote)));
    }

    [Theory]
    [InlineData(Chamber.House, PublishedChamber.House)]
    [InlineData(Chamber.Senate, PublishedChamber.Senate)]
    public async Task TheManifestEntryNamesTheChamberOfTheVoteRevisionSoAPageCanListOneChamberWithoutReadingTheVoteFile(Chamber chamber, PublishedChamber expected)
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', chamber: chamber,
            totalLines: chamber == Chamber.Senate ? ["Count: Yea 1, Nay 0, Present 0, Not voting 0"] : null);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var entry = Assert.Single(ReadManifest(scenario, summary.DirectoryName).Records, record => record.Kind == PublishedRecordKind.Vote);
        Assert.Equal(expected, entry.Chamber);
    }

    [Theory]
    [InlineData(VoteSource.CongressGov, PublishedVoteSource.CongressGov)]
    [InlineData(VoteSource.Voteview, PublishedVoteSource.Voteview)]
    public async Task TheRecordNamesTheDataSourceOfItsRevisionSoAReaderKnowsWhoseDataTheCastsRestOn(VoteSource source, PublishedVoteSource expected)
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', source: source, chamber: source == VoteSource.CongressGov ? Chamber.House : Chamber.Senate,
            totalLines: source == VoteSource.CongressGov ? null : ["Count: Yea 1, Nay 0, Present 0, Not voting 0"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var record = JsonSerializer.Deserialize<PublishedVote>(scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordDataPath(vote)],
            PublicationProtocol.JsonOptions)!;
        Assert.Equal(expected, record.Source);
    }

    [Fact]
    public async Task APersonEditedVoteWithItsApprovalPublishes()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', origin: ReviewTextOrigin.Person);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal(1, summary.RecordCount);
    }

    [Fact]
    public async Task AVoteAlreadyPublishedAtItsCurrentRevisionIsRejectedSoReleasesDoNotRepeatIt()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", vote));

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", vote)));

        Assert.Equal($"Vote {vote} is already published at its current revision.", error.Message);
    }

    [Fact]
    public async Task TheBillLinksToTheActionRecordOfTheReleaseWhenTheActionIsPublishedEarlier()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Action, action)));
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", vote));

        Assert.Equal($"<html>vote {vote} bill {action}</html>", PageOf(scenario, summary, vote));
    }

    [Fact]
    public async Task TheBillLinksToAnActionAddedInTheSameReleaseBecauseActionsLoadBeforeVotes()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, vote), new PublishedRecordReference(PublishedRecordKind.Action, action)));

        Assert.Equal($"<html>vote {vote} bill {action}</html>", PageOf(scenario, summary, vote));
    }

    [Fact]
    public async Task TheBillShowsAsTextWhenTheActionRecordIsNotInTheRelease()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', billActionRecordIds: [new string('c', 64)]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal($"<html>vote {vote} bill </html>", PageOf(scenario, summary, vote));
    }

    [Fact]
    public async Task TheLinkUsesTheActionThatTheReleaseHoldsWhenSeveralSourcesDraftedTheSameBill()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Action, action)));
        var vote = scenario.AddVote('d', billActionRecordIds: [new string('0', 64), action]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", vote));

        Assert.Equal($"<html>vote {vote} bill {action}</html>", PageOf(scenario, summary, vote));
    }

    [Fact]
    public async Task TheVotePageAndTheListPagesShowTheBillLineOfTheActionOnItsBillBecauseOneMapOfTheBuildFeedsEveryPage()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c', summaryText: "This bill funds rent aid. It adds a cap.");
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, vote), new PublishedRecordReference(PublishedRecordKind.Action, action)));

        Assert.Equal($"<html>vote {vote} bill {action} line This bill funds rent aid.</html>", PageOf(scenario, summary, vote));
        var index = Encoding.UTF8.GetString(scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.AllRecordsPagePath(1)]);
        Assert.Contains("lines [c:This bill funds rent aid.,d:This bill funds rent aid.]", index, StringComparison.Ordinal);
        var facet = Encoding.UTF8.GetString(scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.PolicyAreaPagePath("housing-development", 1)]);
        Assert.Contains("lines [c:This bill funds rent aid.", facet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHomeHighlightsGetTheSameBillLinesAsTheListPages()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c', summaryText: "This bill funds rent aid.");
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, vote), new PublishedRecordReference(PublishedRecordKind.Action, action)));

        Assert.Equal("This bill funds rent aid.", scenario.Highlights!.BillLines.BillOfVote(vote)!.Line);
    }

    [Fact]
    public async Task ACarriedVotePageRendersAgainWhenAnActionWithABillLineJoinsItsBillAndStaysAsItWasWhenAnUnrelatedRecordPublishes()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c', summaryText: "This bill funds rent aid.");
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", vote));
        scenario.PageLayout = " layout-2";

        var unrelated = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b'))));
        var joined = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3",
            new PublishedRecordReference(PublishedRecordKind.Action, action)));

        Assert.Equal($"<html>vote {vote} bill </html>", PageOf(scenario, unrelated, vote));
        Assert.Equal($"<html>vote {vote} bill {action} line This bill funds rent aid. layout-2</html>", PageOf(scenario, joined, vote));
    }

    [Fact]
    public async Task ACarriedVotePageGetsTheBillLinkWhenAnActionWithNoBillLineJoinsItsBill()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", vote));
        scenario.PageLayout = " layout-2";

        var joined = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Action, action)));

        Assert.Equal($"<html>vote {vote} bill {action} layout-2</html>", PageOf(scenario, joined, vote));
    }

    [Fact]
    public async Task ACarriedVotePageStaysAsItWasWhenANewRevisionOfItsActionKeepsTheSameBillLine()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c', summaryText: "This bill funds rent aid.");
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, vote), new PublishedRecordReference(PublishedRecordKind.Action, action)));
        scenario.PageLayout = " layout-2";
        scenario.ReviseAction(action, 2);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Action, action)));

        Assert.DoesNotContain("layout-2", PageOf(scenario, summary, vote), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARepublishedVoteReplacesItsCarriedEntryAndKeepsItsFirstPublicationTime()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d');
        var firstPublishedAt = scenario.Clock.Now;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", vote));
        var review = scenario.Votes.Publishable[vote].Review;
        var revised = review.CurrentRevision with { RevisionNumber = 2, TrackedIssueIds = ["transit"] };
        scenario.Votes.Publishable[vote] = scenario.Votes.Publishable[vote] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(PublicationScenario.Decision(vote, 2, ReviewDecisionKind.Approve) with { RevisionNumber = 2, CorrectionNote = "Fixed the question." }),
                ReviewStateVersion = 2
            }
        };
        scenario.Clock.Now = firstPublishedAt.AddDays(1);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", vote));

        var entry = Assert.Single(ReadManifest(scenario, second.DirectoryName).Records);
        Assert.Equal((2, firstPublishedAt), (entry.RevisionNumber, entry.FirstPublishedAtUtc));
        Assert.Equal(["transit"], entry.IssueIds);
    }

    [Fact]
    public async Task ARequestWithOnlyVotesNeverAsksTheOtherStores()
    {
        var scenario = new PublicationScenario();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", scenario.AddVote('d')));

        Assert.Equal((0, 0), (scenario.Reviews.ReadCount, scenario.Actions.PublicationReadCount));
    }

    [Fact]
    public async Task TheRecordCarriesTheResultTheTotalsAndEachPartyMajorityFromTheSavedLines()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers = [Member("M000001", Term("Democrat")), .. new[] { "R1", "R2", "R3", "R4", "R5" }.Select(id => Member(id, Term("Republican")))];
        var vote = scenario.AddVote('d', totalLines: [PublicationScenario.DefaultPartyTotal, "Party total R: Yea 2, Nay 1, Present 1, Not voting 1"],
            otherMemberLines: ["Member: R1, A, R, AL, Aye", "Member: R2, Smith, Jr., R, AL, Yea", "Member: R3, C, R, AL, No", "Member: R4, D, R, AL, Not Voting",
                "Member: R5, E, R, AL, Present"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var record = ReadVote(scenario, summary, vote);
        Assert.Equal("Passed", record.Result);
        Assert.Equal(new PublishedVoteTotals { Yea = 3, Nay = 1, Present = 1, NotVoting = 1 }, record.Totals);
        Assert.Equal([("Democrat", 1, 0, "Yea"), ("Republican", 2, 1, "Yea")],
            record.PartyMajorities.Select(party => (party.Party, party.YeaCount, party.NayCount, party.Majority)));
        var row = Assert.Single(ReadOfficialVotes(scenario, summary, "mayor").Votes);
        Assert.Equal(("Passed", "Democrat", "Yea", 3), (row.Result, row.Party, row.PartyMajority, row.Totals!.Yea));
        Assert.False(row.SplitVote);
    }

    [Fact]
    public async Task TheOfficialRowIsFlaggedSplitWhenMostDemocratsAndMostRepublicansVotedOnOppositeSides()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers = [Member("M000001", Term("Democrat")), Member("R1", Term("Republican"))];
        var vote = scenario.AddVote('d', totalLines: [PublicationScenario.DefaultPartyTotal, "Party total R: Yea 0, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: R1, A, R, AL, Nay"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.True(Assert.Single(ReadOfficialVotes(scenario, summary, "mayor").Votes).SplitVote);
    }

    [Fact]
    public async Task PresentAndNotVotingStayOutOfTheMajority()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers = [Member("M000001", Term("Republican")), Member("R1", Term("Republican")), Member("R2", Term("Republican"))];
        var vote = scenario.AddVote('d', totalLines: ["Party total D: Yea 1, Nay 1, Present 1, Not voting 0"],
            otherMemberLines: ["Member: R1, A, D, AL, Nay", "Member: R2, B, D, AL, Present"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var party = Assert.Single(ReadVote(scenario, summary, vote).PartyMajorities);
        Assert.Equal(("Republican", 1, 1, null), (party.Party, party.YeaCount, party.NayCount, party.Majority));
    }

    [Fact]
    public async Task ATiedPartyHasNoMajorityOnItsOfficialRow()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers = [Member("M000001", Term("Democrat")), Member("D2", Term("Democrat"))];
        var vote = scenario.AddVote('d', totalLines: ["Party total D: Yea 1, Nay 1, Present 0, Not voting 0"], otherMemberLines: ["Member: D2, A, D, AL, Nay"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var row = Assert.Single(ReadOfficialVotes(scenario, summary, "mayor").Votes);
        Assert.Equal((null, false), (row.PartyMajority, row.SplitVote));
    }

    [Theory]
    [InlineData("Independent")]
    [InlineData("Unknown")]
    public async Task AMemberOfAGroupWithNoYeaOrNayVotesStillPublishesARowOnASplitVote(string party)
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers = [Member("M000001", Term(party)), Member("D1", Term("Democrat")), Member("R1", Term("Republican"))];
        var vote = scenario.AddVote('d', cast: "Not Voting",
            totalLines: ["Party total D: Yea 1, Nay 0, Present 0, Not voting 1", "Party total R: Yea 0, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: D1, A, D, AL, Yea", "Member: R1, A, R, AL, Nay"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var row = Assert.Single(ReadOfficialVotes(scenario, summary, "mayor").Votes);
        Assert.Equal((party, null, true), (row.Party, row.PartyMajority, row.SplitVote));
    }

    [Fact]
    public async Task ATiedIndependentGroupStillPublishesARowOnASplitVote()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers = [Member("M000001", Term("Independent")), Member("I1", Term("Independent")), Member("D1", Term("Democrat")), Member("R1", Term("Republican"))];
        var vote = scenario.AddVote('d',
            totalLines: ["Party total D: Yea 2, Nay 0, Present 0, Not voting 0", "Party total R: Yea 0, Nay 1, Present 0, Not voting 0", "Party total I: Yea 0, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: D1, A, D, AL, Yea", "Member: R1, A, R, AL, Nay", "Member: I1, A, I, VT, Nay"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var row = Assert.Single(ReadOfficialVotes(scenario, summary, "mayor").Votes);
        Assert.Equal(("Independent", null, true), (row.Party, row.PartyMajority, row.SplitVote));
    }

    // The source gives a bare "I" for an Independent, but the member votes with a party caucus.
    [Fact]
    public async Task AnIndependentWhoCaucusesWithDemocratsCountsAsDemocrat()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers = [Member("M000001", Term("Democrat")), Member("I1", Term("Independent", caucus: "Democrat"))];
        var vote = scenario.AddVote('d', totalLines: [PublicationScenario.DefaultPartyTotal, "Party total I: Yea 0, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: I1, A, I, VT, Nay"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var party = Assert.Single(ReadVote(scenario, summary, vote).PartyMajorities);
        Assert.Equal(("Democrat", 1, 1), (party.Party, party.YeaCount, party.NayCount));
    }

    // The vote day is 2025-04-10, inside the first term. A lookup that ignored the date would take the last term.
    [Fact]
    public async Task AMemberWhoSwitchedPartyCountsWithTheTermThatCoversTheVoteDay()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers =
        [
            Member("M000001", new RosterTerm(0, "rep", new(2025, 1, 3), new(2025, 4, 30), "MA", 1, null, "Republican", null),
                new RosterTerm(1, "rep", new(2025, 5, 1), new(2027, 1, 3), "MA", 1, null, "Democrat", null))
        ];
        var vote = scenario.AddVote('d');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal("Republican", Assert.Single(ReadVote(scenario, summary, vote).Members).Party);
    }

    // The stored parties pin the member list, so the file, the list parties, and the alignment all follow them and not the roster of today.
    // Without the pin the roster gives M000001 the Republican term. Republican would then tie, and the vote would have no alignment.
    [Fact]
    public async Task AMemberListThatStoresPartiesPinsThemSoTheFileTheListPartiesAndTheAlignmentIgnoreTheRoster()
    {
        var scenario = PinnedScenario(out var vote, out _);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var file = ReadVote(scenario, summary, vote);
        var columns = Assert.Single(scenario.Publications.LastCommit!.VoteColumns);
        Assert.Equal(["Democrat"], file.Members.Select(member => member.Party));
        Assert.Equal(["Democrat"], columns.MemberParties.ToArray());
        Assert.Equal((PublishedVoteAlignment.Split, "Yea", "Nay"), (file.AlignmentOfParties(), file.MajorityOf(PublishedPartyMajority.Democrat), file.MajorityOf(PublishedPartyMajority.Republican)));
        Assert.Equal((file.AlignmentOfParties(), file.MajorityOf(PublishedPartyMajority.Democrat), file.MajorityOf(PublishedPartyMajority.Republican)),
            (columns.Alignment, columns.DemocratMajority, columns.RepublicanMajority));
    }

    [Fact]
    public async Task ARevisionWithPinnedPartiesNeverStopsTheOtherRecordsOfTheSameBatch()
    {
        var scenario = PinnedScenario(out var vote, out var other);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Vote, vote), new PublishedRecordReference(PublishedRecordKind.Vote, other)));

        Assert.Equal(2, summary.RecordCount);
        Assert.Equal(["Democrat"], ReadVote(scenario, summary, vote).Members.Select(member => member.Party));
        Assert.Equal(["Democrat"], ReadVote(scenario, summary, other).Members.Select(member => member.Party));
    }

    private static PublicationScenario PinnedScenario(out string vote, out string other)
    {
        var scenario = new PublicationScenario
        {
            RosterMembers = [Member("M000001", Term("Republican")), Member("D1", Term("Democrat")), Member("R1", Term("Republican"))]
        };
        string[] totals = ["Party total D: Yea 2, Nay 0, Present 0, Not voting 0", "Party total R: Yea 0, Nay 1, Present 0, Not voting 0"];
        string[] lines = ["Member: D1, A, D, AL, Yea", "Member: R1, A, R, AL, Nay"];
        vote = scenario.AddVote('d', totalLines: totals, otherMemberLines: lines);
        other = scenario.AddVote('e', totalLines: totals, otherMemberLines: lines);
        scenario.Votes.Publishable[vote] = scenario.Votes.Publishable[vote] with { StoredParties = ImmutableDictionary<string, string>.Empty.Add("M000001", "Democrat") };
        scenario.Votes.Publishable[other] = scenario.Votes.Publishable[other] with { StoredParties = ImmutableDictionary<string, string>.Empty.Add("M000001", "Democrat") };
        return scenario;
    }

    // The two terms share 2025-04-10. The term that starts that day wins, because a new term replaces the old one on its first day.
    [Fact]
    public async Task OnTheDayOneTermEndsAndTheNextStartsTheNewTermWins()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers =
        [
            Member("M000001", new RosterTerm(0, "rep", new(2023, 1, 3), new(2025, 4, 10), "MA", 1, null, "Republican", null),
                new RosterTerm(1, "rep", new(2025, 4, 10), new(2027, 1, 3), "MA", 1, null, "Democrat", null))
        ];
        var vote = scenario.AddVote('d');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal("Democrat", Assert.Single(ReadVote(scenario, summary, vote).Members).Party);
    }

    [Fact]
    public async Task WhenTermsOverlapTheOneThatStartsLatestWins()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers =
        [
            Member("M000001", new RosterTerm(0, "rep", new(2025, 1, 3), new(2027, 1, 3), "MA", 1, null, "Republican", null),
                new RosterTerm(1, "rep", new(2025, 3, 1), new(2027, 1, 3), "MA", 1, null, "Democrat", null))
        ];
        var vote = scenario.AddVote('d');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal("Democrat", Assert.Single(ReadVote(scenario, summary, vote).Members).Party);
    }

    // A member who moved from the House to the Senate holds a Senate term on the same day. A House vote must use the House term.
    [Fact]
    public async Task AHouseVoteUsesTheHouseTermOfAMemberWhoHoldsASenateTermOnTheSameDay()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers =
        [
            Member("M000001", new RosterTerm(0, "rep", new(2023, 1, 3), new(2025, 4, 30), "MA", 1, null, "Republican", null),
                new RosterTerm(1, "sen", new(2025, 4, 10), new(2031, 1, 3), "MA", null, 1, "Democrat", null))
        ];
        var vote = scenario.AddVote('d');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal("Republican", Assert.Single(ReadVote(scenario, summary, vote).Members).Party);
    }

    // The roster holds current members only, so a member who left has no term and takes the party of the letter on the vote line.
    [Fact]
    public async Task AMemberWhoLeftTheRosterTakesThePartyOfTheSourceLetter()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: [PublicationScenario.DefaultPartyTotal, "Party total R: Yea 0, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: X1, A, R, AL, Nay"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal([("Democrat", 1, 0), ("Republican", 0, 1)],
            ReadVote(scenario, summary, vote).PartyMajorities.Select(party => (party.Party, party.YeaCount, party.NayCount)));
    }

    [Fact]
    public async Task AMemberWhoLeftTheRosterAndHasAnUnknownLetterCountsUnderUnknown()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: [PublicationScenario.DefaultPartyTotal, "Party total Q: Yea 0, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: X1, A, Q, AL, Nay"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal([("Democrat", 1, 0), (PublishedPartyMajority.UnknownParty, 0, 1)],
            ReadVote(scenario, summary, vote).PartyMajorities.Select(party => (party.Party, party.YeaCount, party.NayCount)));
    }

    [Fact]
    public async Task VoteviewSenatorsTakeTheRosterPartyByBioguideIdEvenWhenTheMemberLineNamesAnotherParty()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers = [new RosterMember("M000001", "Mayor Example", null, null, null, [Term("Democrat", chamber: "sen")]), new RosterMember("R9", "R9", null, null, null, [Term("Republican", chamber: "sen")])];
        var vote = scenario.AddVote('d', chamber: Chamber.Senate, source: VoteSource.Voteview, totalLines: ["Count: Yea 1, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: R9, A, D, AL, Nay"], trackedSourceId: "M000001");

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var record = ReadVote(scenario, summary, vote);
        Assert.Equal(new PublishedVoteTotals { Yea = 1, Nay = 1, Present = 0, NotVoting = 0 }, record.Totals);
        Assert.Equal(["Democrat", "Republican"], record.PartyMajorities.Select(party => party.Party));
    }

    // A wrong count or a wrong member line would publish a false total next to cited text, so the vote must not publish.
    [Theory]
    [InlineData("Party total D: Yea 0, Nay 1, Present 0, Not voting 0")]
    [InlineData("Party total R: Yea 1, Nay 0, Present 0, Not voting 0")]
    public async Task AMemberLineThatDisagreesWithTheTotalLinesRefusesTheVoteAndStagesNothing(string total)
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: [total]);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote)));

        Assert.Contains("do not add up", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task AHouseVoteWithNoPartyTotalLinesRefusesBecauseItHasNoTotalsToShow()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: []);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote)));

        Assert.Contains("no party total lines", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASpeakerElectionPublishesItsCandidateTotalsWithNoTotalsAndNoPartyMajorities()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: ["Candidate total Emmer: 1", "Candidate total Jeffries: 215", "Candidate total Johnson (LA): 218"],
            otherMemberLines: ["Member: X1, A, R, AL, Emmer"], cast: "Johnson (LA)", result: "Johnson (LA)");

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var record = ReadVote(scenario, summary, vote);
        Assert.Equal(("Johnson (LA)", null, 0), (record.Result, record.Totals, record.PartyMajorities.Length));
        var member = Assert.Single(record.Members);
        Assert.Equal(("Democrat", "Johnson (LA)"), (member.Party, member.Cast));
        var row = Assert.Single(ReadOfficialVotes(scenario, summary, "mayor").Votes);
        Assert.Equal(("Johnson (LA)", null, "Democrat", null, "Johnson (LA)", false), (row.Result, row.Totals, row.Party, row.PartyMajority, row.Cast, row.SplitVote));
    }

    // A stray candidate line beside party totals must not skip the total and party letter checks.
    [Fact]
    public async Task AVoteWithPartyTotalsAndACandidateLineKeepsEveryCheckAndRefusesAMismatch()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: ["Party total D: Yea 0, Nay 1, Present 0, Not voting 0", "Candidate total Emmer: 1"]);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote)));

        Assert.Contains("do not add up", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    // A party total line with no counts has no ": ", and the failure must name the vote, not crash the whole publish.
    [Fact]
    public async Task APartyTotalLineWithNoCountsRefusesTheVoteBySayingWhichVote()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: ["Party total R:"]);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote)));

        Assert.Contains(vote, error.Message, StringComparison.Ordinal);
        Assert.Contains("no counts", error.Message, StringComparison.Ordinal);
    }

    // The lines come from the collection renderers, so a change to a label or a field order in a renderer fails here and not in production.
    [Fact]
    public async Task HouseLinesFromTheRealRenderersPublishWithTheirTotalsAndParties()
    {
        var detail = new HouseVoteDetail(HouseVoteFixtures.Vote(100, HouseVoteFixtures.Updated), null, null, null, "Passed", null, null,
            [new("D", 1, 0, 0, 0), new("R", 1, 1, 0, 0)], [], null);
        var members = new HouseVoteMemberVotes(detail.Vote,
            [new("X1", "Ann", "Roe", "R", "AL", "Aye"), new("X2", "Bo", "Poe", "R", "AL", "No")]);
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: LinesOf(HouseVoteRenderer.RenderDetail(detail), "Party total "),
            otherMemberLines: LinesOf(HouseVoteRenderer.RenderMembers(members), "Member: "));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var record = ReadVote(scenario, summary, vote);
        Assert.Equal(new PublishedVoteTotals { Yea = 2, Nay = 1, Present = 0, NotVoting = 0 }, record.Totals);
        Assert.Equal([("Democrat", 1, 0), ("Republican", 1, 1)],
            record.PartyMajorities.Select(party => (party.Party, party.YeaCount, party.NayCount)));
    }

    [Fact]
    public async Task VoteviewLinesFromTheRealRendererPublishWithTheirTotalsAndParties()
    {
        var text = VoteviewRenderer.Render(VoteviewRollOf(VoteviewMemberVoteOf("S1", "D", "Yea"), VoteviewMemberVoteOf("S9", "R", "Nay")));
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', chamber: Chamber.Senate, source: VoteSource.Voteview, trackedSourceId: "S1",
            totalLines: LinesOf(text, "Count: "), otherMemberLines: [.. LinesOf(text, "Member: ").Where(line => !line.StartsWith("Member: S1,", StringComparison.Ordinal))]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        var record = ReadVote(scenario, summary, vote);
        Assert.Equal(new PublishedVoteTotals { Yea = 1, Nay = 1, Present = 0, NotVoting = 0 }, record.Totals);
        Assert.Equal(["Democrat", "Republican"], record.PartyMajorities.Select(party => party.Party));
    }

    // Four different counts, with one tracked Democrat Yea, show that each label of the real renderer lands in its own total, so a swap between two labels fails here.
    [Fact]
    public async Task EachHouseCountLabelFromTheRealRendererPublishesIntoItsOwnTotal()
    {
        var detail = new HouseVoteDetail(HouseVoteFixtures.Vote(100, HouseVoteFixtures.Updated), null, null, null, "Passed", null, null,
            [new("D", 1, 0, 0, 0), new("R", 3, 3, 2, 1)], [], null);
        var members = new HouseVoteMemberVotes(detail.Vote,
            [.. Members("H", "Aye", 3), .. Members("N", "No", 3), .. Members("P", "Present", 2), .. Members("V", "Not Voting", 1)]);
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: LinesOf(HouseVoteRenderer.RenderDetail(detail), "Party total "),
            otherMemberLines: LinesOf(HouseVoteRenderer.RenderMembers(members), "Member: "));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal(new PublishedVoteTotals { Yea = 4, Nay = 3, Present = 2, NotVoting = 1 }, ReadVote(scenario, summary, vote).Totals);
    }

    [Fact]
    public async Task EachVoteviewCountLabelFromTheRealRendererPublishesIntoItsOwnTotal()
    {
        var text = VoteviewRenderer.Render(VoteviewRollOf([
            VoteviewMemberVoteOf("S1", "D", "Yea"), .. VoteviewMembers("A", "Yea", 3), .. VoteviewMembers("B", "Nay", 3), .. VoteviewMembers("C", "Present", 2), .. VoteviewMembers("D", "Not Voting", 1)]));
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', chamber: Chamber.Senate, source: VoteSource.Voteview, trackedSourceId: "S1",
            totalLines: LinesOf(text, "Count: "), otherMemberLines: [.. LinesOf(text, "Member: ").Where(line => !line.StartsWith("Member: S1,", StringComparison.Ordinal))]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal(new PublishedVoteTotals { Yea = 4, Nay = 3, Present = 2, NotVoting = 1 }, ReadVote(scenario, summary, vote).Totals);
    }

    private static HouseVoteMember[] Members(string idPrefix, string cast, int count) =>
        [.. Enumerable.Range(1, count).Select(index => new HouseVoteMember($"{idPrefix}{index}", "Ann", "Roe", "R", "AL", cast))];

    private static VoteviewMemberVote[] VoteviewMembers(string idPrefix, string cast, int count) =>
        [.. Enumerable.Range(1, count).Select(index => VoteviewMemberVoteOf($"{idPrefix}{index}", "R", cast))];

    private static VoteviewMemberVote VoteviewMemberVoteOf(string bioguideId, string party, string cast) =>
        new(new VoteviewMember(bioguideId.GetHashCode(StringComparison.Ordinal), bioguideId, "Roe", party, "AL"), cast);

    private static VoteviewRoll VoteviewRollOf(params VoteviewMemberVote[] votes) =>
        new(new VoteviewRollCall(1, new SenateVoteRoll(new VoteSession(119, 1), 1), new DateOnly(2025, 4, 10), "On Passage", "Title", "Passed",
            null, null, null, 0, 0), votes);

    private static string[] LinesOf(string text, string prefix) =>
        [.. text.Split('\n').Where(line => line.StartsWith(prefix, StringComparison.Ordinal))];

    private static RosterTerm Term(string party, string? caucus = null, string chamber = "rep") =>
        new(0, chamber, new(2025, 1, 3), new(2027, 1, 3), "MA", 1, null, party, caucus);

    private static RosterMember Member(string id, params RosterTerm[] terms) => new(id, id, null, null, null, terms);

    private static PublishedVote ReadVote(PublicationScenario scenario, PublicationReleaseSummary summary, string recordId) =>
        JsonSerializer.Deserialize<PublishedVote>(scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordDataPath(recordId)],
            PublicationProtocol.JsonOptions)!;

    private static PublishedOfficialVotes ReadOfficialVotes(PublicationScenario scenario, PublicationReleaseSummary summary, string officialId) =>
        JsonSerializer.Deserialize<PublishedOfficialVotes>(
            scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.OfficialVotesPath(officialId, 119)], PublicationProtocol.JsonOptions)!;

    private static string PageOf(PublicationScenario scenario, PublicationReleaseSummary summary, string recordId) =>
        Encoding.UTF8.GetString(scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordPagePath(recordId)]);

    private static PublishReleaseRequest Request(string key, string voteId) =>
        PublicationScenario.Request(key, new PublishedRecordReference(PublishedRecordKind.Vote, voteId));

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
