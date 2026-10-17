using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
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

    private static string PageOf(PublicationScenario scenario, PublicationReleaseSummary summary, string recordId) =>
        Encoding.UTF8.GetString(scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordPagePath(recordId)]);

    private static PublishReleaseRequest Request(string key, string voteId) =>
        PublicationScenario.Request(key, new PublishedRecordReference(PublishedRecordKind.Vote, voteId));

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
