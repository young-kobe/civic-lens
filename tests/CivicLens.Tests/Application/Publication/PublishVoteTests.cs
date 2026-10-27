using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Core.Votes;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

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
                Decisions = review.Decisions.Add(PublicationScenario.Decision(vote, 2, ReviewDecisionKind.Approve) with { RevisionNumber = 2 }),
                ReviewStateVersion = 2
            }
        };
        scenario.Clock.Now = firstPublishedAt.AddDays(1);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", vote) with { Notes = [new(PublishedRecordKind.Vote, vote, "Fixed the question.")] });

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
        Assert.Equal(("Passed", "Democrat", "Yea", 3), (row.Result, row.Party, row.PartyMajority, row.Totals.Yea));
    }

    // Present is not a side. If it counted as a Nay, these Republicans would tie and the party would show no majority.
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

        Assert.Null(Assert.Single(ReadOfficialVotes(scenario, summary, "mayor").Votes).PartyMajority);
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

    [Fact]
    public async Task AMemberWhoSwitchedPartyCountsWithTheTermThatCoversTheVoteDay()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers =
        [
            Member("M000001", new RosterTerm(0, "rep", new(2025, 1, 3), new(2025, 4, 1), "MA", 1, null, "Republican", null),
                new RosterTerm(1, "rep", new(2025, 4, 2), new(2027, 1, 3), "MA", 1, null, "Democrat", null))
        ];
        var vote = scenario.AddVote('d');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal("Democrat", Assert.Single(ReadVote(scenario, summary, vote).Members).Party);
    }

    [Fact]
    public async Task AMemberOutsideTheRosterUsesTheConfiguredOfficeThenAnUnknownPartyThatStaysCounted()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("M000001", null);
        scenario.Offices["mayor"] = [new() { Title = "rep", StartsOn = new(2025, 1, 3), Party = "Green" }];
        var vote = scenario.AddVote('d', totalLines: [PublicationScenario.DefaultPartyTotal, "Party total R: Yea 0, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: X1, A, R, AL, Nay"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", vote));

        Assert.Equal([("Green", 1, 0), (PublishedPartyMajority.UnknownParty, 0, 1)],
            ReadVote(scenario, summary, vote).PartyMajorities.Select(party => (party.Party, party.YeaCount, party.NayCount)));
    }

    [Fact]
    public async Task SenateTotalsComeFromTheCountLineAndPartiesFromTheLisIdsOfTheRoster()
    {
        var scenario = new PublicationScenario();
        scenario.RosterMembers = [new RosterMember("M000001", "S1", "Mayor Example", null, null, null, [Term("Democrat")]), new RosterMember("R9", "S9", "R9", null, null, null, [Term("Republican")])];
        var vote = scenario.AddVote('d', chamber: Chamber.Senate, totalLines: ["Count: Yea 1, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: S9, A, R, AL, Nay"], trackedSourceId: "S1");

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

    private static RosterTerm Term(string party, string? caucus = null) => new(0, "rep", new(2025, 1, 3), new(2027, 1, 3), "MA", 1, null, party, caucus);

    private static RosterMember Member(string id, params RosterTerm[] terms) => new(id, null, id, null, null, null, terms);

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
