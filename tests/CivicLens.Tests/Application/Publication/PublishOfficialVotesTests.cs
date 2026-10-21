using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishOfficialVotesTests
{
    private static readonly string MayorFile = PublicationProtocol.OfficialVotesPath("mayor", 119);
    private static readonly string ChairFile = PublicationProtocol.OfficialVotesPath("chair", 119);

    [Fact]
    public async Task AVoteRowCarriesTheRollCallBillCastAndHeadlineSoTheProfilePageNeedsNoRecordFile()
    {
        var scenario = new PublicationScenario();

        var summary = await PublishVotesAsync(scenario, "k", scenario.AddVote('a'));

        var row = Assert.Single(VotesOf(scenario, summary.DirectoryName, MayorFile).Votes);
        Assert.Equal((new string('a', 64), new DateOnly(2025, 4, 10), 119, 1, 100), (row.RecordId, row.VotedOn, row.Congress, row.Session, row.RollCallNumber));
        Assert.Equal(("hr", 7, "Yea", "Vote a"), (row.BillType, row.BillNumber, row.Cast, row.Headline));
    }

    [Fact]
    public async Task APublishThatAddsOneVoteRewritesOnlyItsMembersFilesAndLinksEveryOtherVoteFile()
    {
        var scenario = new PublicationScenario();
        var first = await PublishVotesAsync(scenario, "k1", scenario.AddVote('a', memberIds: ["mayor"]));
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        await PublishVotesAsync(scenario, "k2", scenario.AddVote('b', memberIds: ["chair"]));

        Assert.Contains(ChairFile, scenario.Releases.WriteOrder);
        Assert.DoesNotContain(MayorFile, scenario.Releases.WriteOrder);
        Assert.Contains((first.DirectoryName, MayorFile), scenario.Releases.Links);
    }

    [Fact]
    public async Task AMemberOfTheNewVoteKeepsEarlierRowsOfTheSameCongressNewestFirst()
    {
        var scenario = new PublicationScenario();
        await PublishVotesAsync(scenario, "k1", scenario.AddVote('a'));

        var second = await PublishVotesAsync(scenario, "k2", scenario.AddVote('b'));

        Assert.Equal([new string('a', 64), new string('b', 64)],
            VotesOf(scenario, second.DirectoryName, MayorFile).Votes.Select(vote => vote.RecordId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AVoteOfAnotherCongressGoesToItsOwnFileAndLeavesTheFirstFileLinked()
    {
        var scenario = new PublicationScenario();
        var first = await PublishVotesAsync(scenario, "k1", scenario.AddVote('a'));
        scenario.Releases.Links.Clear();

        var second = await PublishVotesAsync(scenario, "k2", scenario.AddVote('b', congress: 118));

        Assert.Contains((first.DirectoryName, MayorFile), scenario.Releases.Links);
        Assert.Single(VotesOf(scenario, second.DirectoryName, PublicationProtocol.OfficialVotesPath("mayor", 118)).Votes);
    }

    [Fact]
    public async Task AReplacedVoteRevisionReplacesItsRowInsteadOfAddingADuplicate()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('a');
        await PublishVotesAsync(scenario, "k1", vote);
        Revise(scenario, vote, headline: "Vote a, second revision", members: ["mayor"]);

        var second = await ReplaceVotesAsync(scenario, "k2", vote);

        var row = Assert.Single(VotesOf(scenario, second.DirectoryName, MayorFile).Votes);
        Assert.Equal("Vote a, second revision", row.Headline);
    }

    [Fact]
    public async Task AnOfficialWhoLeftTheMemberListLosesTheRowEvenWhenAnotherRecordKeepsTheOfficialListed()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('a');
        var statement = scenario.AddStatement('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, vote), new PublishedRecordReference(PublishedRecordKind.Statement, statement)));
        Revise(scenario, vote, headline: "Vote a", members: ["chair"]);

        var second = await ReplaceVotesAsync(scenario, "k2", vote);

        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.DoesNotContain(MayorFile, files.Keys);
        Assert.Equal([new string('a', 64)], VotesOf(scenario, second.DirectoryName, ChairFile).Votes.Select(item => item.RecordId));
        Assert.Contains("mayor", Officials(scenario, second.DirectoryName).Officials.Select(official => official.Id));
    }

    [Fact]
    public async Task AVoteFileOverTheByteCapIsRefusedOnReadSoAHugeFileCannotExhaustMemory()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(withFiles: true, []);
        scenario.Releases.Directories["seeded"][MayorFile] = new byte[PublishedOfficialVotes.MaximumBytes + 1];

        await Assert.ThrowsAsync<InvalidDataException>(() => PublishVotesAsync(scenario, "k", scenario.AddVote('a')));
    }

    [Fact]
    public async Task APublishThatReplacesTwoVotesOfDifferentCongressesRewritesTheFileOfEachCongress()
    {
        var scenario = new PublicationScenario();
        var recent = scenario.AddVote('a');
        var older = scenario.AddVote('b', congress: 118);
        await PublishVotesAsync(scenario, "k1", recent, older);
        Revise(scenario, recent, headline: "Recent, second revision", members: ["mayor"]);
        Revise(scenario, older, headline: "Older, second revision", members: ["chair"]);

        var second = await ReplaceVotesAsync(scenario, "k2", recent, older);

        Assert.Equal(["Recent, second revision"], VotesOf(scenario, second.DirectoryName, MayorFile).Votes.Select(vote => vote.Headline));
        Assert.DoesNotContain(PublicationProtocol.OfficialVotesPath("mayor", 118), scenario.Releases.Directories[second.DirectoryName].Keys);
        Assert.Equal(["Older, second revision"],
            VotesOf(scenario, second.DirectoryName, PublicationProtocol.OfficialVotesPath("chair", 118)).Votes.Select(vote => vote.Headline));
    }

    [Fact]
    public async Task AVoteFileHoldingAVoteOfAnotherCongressIsRefusedSoAMisplacedFileCannotHideARow()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(withFiles: true, []);
        var misplaced = PublishedOfficialVotes.Of([new PublishedOfficialVote
        {
            RecordId = new string('f', 64), VotedOn = new DateOnly(2024, 1, 1), Congress = 118, Session = 1, RollCallNumber = 1, Cast = "Yea", Headline = "Misplaced"
        }]);
        scenario.Releases.Directories["seeded"][MayorFile] = JsonSerializer.SerializeToUtf8Bytes(misplaced, PublicationProtocol.JsonOptions);

        await Assert.ThrowsAsync<InvalidDataException>(() => PublishVotesAsync(scenario, "k", scenario.AddVote('a')));
    }

    [Fact]
    public async Task AnOfficialWithNoVoteGetsNoVoteFile()
    {
        var scenario = new PublicationScenario();

        var summary = await PublishVotesAsync(scenario, "k", scenario.AddVote('a', memberIds: ["mayor"]));

        Assert.DoesNotContain(ChairFile, scenario.Releases.Directories[summary.DirectoryName].Keys);
    }

    private static Task<PublicationReleaseSummary> PublishVotesAsync(PublicationScenario scenario, string key, params string[] votes) =>
        scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request(key, [.. votes.Select(vote => new PublishedRecordReference(PublishedRecordKind.Vote, vote))]));

    private static Task<PublicationReleaseSummary> ReplaceVotesAsync(PublicationScenario scenario, string key, params string[] votes)
    {
        PublishedRecordReference[] references = [.. votes.Select(vote => new PublishedRecordReference(PublishedRecordKind.Vote, vote))];
        return scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.WithNotesFor(PublicationScenario.Request(key, references), references));
    }

    private static void Revise(PublicationScenario scenario, string vote, string headline, string[] members)
    {
        var review = scenario.Votes.Publishable[vote].Review;
        var current = review.CurrentRevision;
        var revised = current with
        {
            RevisionNumber = current.RevisionNumber + 1,
            Headline = headline,
            Members = [.. members.Select(person => current.Members[0] with { PersonId = person })]
        };
        scenario.Votes.Publishable[vote] = scenario.Votes.Publishable[vote] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(PublicationScenario.Decision(vote, revised.RevisionNumber, ReviewDecisionKind.Approve) with { RevisionNumber = revised.RevisionNumber }),
                ReviewStateVersion = revised.RevisionNumber
            }
        };
    }

    private static PublishedOfficialVotes VotesOf(PublicationScenario scenario, string directory, string path) =>
        JsonSerializer.Deserialize<PublishedOfficialVotes>(scenario.Releases.Directories[directory][path], PublicationProtocol.JsonOptions)!;

    private static PublishedOfficials Officials(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublishedOfficials>(scenario.Releases.Directories[directory][PublicationProtocol.OfficialsPath], PublicationProtocol.JsonOptions)!;
}
