using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

/// <summary>The commit pins one bill link for each vote of the release. The links equal the ones that ReleaseBillLines gives the vote pages.</summary>
public sealed class PublishVoteBillLinksTests
{
    [Fact]
    public async Task ACommitLinksAVoteToTheActionOfItsBillThatTheReleaseHoldsEvenWhenTheActionPublishedEarlier()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Reference(action, PublishedRecordKind.Action)));
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", Reference(vote, PublishedRecordKind.Vote)));

        Assert.Equal<ReleaseVoteBill>([new ReleaseVoteBill(vote, action)], [.. scenario.Publications.LastCommit!.VoteBills]);
    }

    [Fact]
    public async Task ACommitLinksTheFirstActionInIdOrderThatTheReleaseHoldsAndSkipsOneThatItDoesNotHold()
    {
        var scenario = new PublicationScenario();
        var later = scenario.AddAction('c');
        var first = scenario.AddAction('b');
        var absent = new string('a', 64);
        var vote = scenario.AddVote('d', billActionRecordIds: [absent, first, later]);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            Reference(vote, PublishedRecordKind.Vote), Reference(later, PublishedRecordKind.Action), Reference(first, PublishedRecordKind.Action)));

        Assert.Equal<ReleaseVoteBill>([new ReleaseVoteBill(vote, first)], [.. scenario.Publications.LastCommit!.VoteBills]);
    }

    [Fact]
    public async Task ACommitWritesNoLinkForAVoteWhoseBillActionIsNotInTheReleaseAndAnActionThatPublishesLaterLinksTheCarriedVote()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Reference(vote, PublishedRecordKind.Vote)));
        Assert.Empty(scenario.Publications.LastCommit!.VoteBills);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", Reference(action, PublishedRecordKind.Action)));

        Assert.Equal<ReleaseVoteBill>([new ReleaseVoteBill(vote, action)], [.. scenario.Publications.LastCommit!.VoteBills]);
    }

    [Fact]
    public async Task ACommitWritesNoLinkForAVoteThatTheReleaseWithdrawsOrForAVoteWhoseActionTheReleaseWithdraws()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);
        var other = scenario.AddVote('e', billActionRecordIds: [action]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            Reference(action, PublishedRecordKind.Action), Reference(vote, PublishedRecordKind.Vote), Reference(other, PublishedRecordKind.Vote)));
        Assert.Equal<ReleaseVoteBill>([new ReleaseVoteBill(vote, action), new ReleaseVoteBill(other, action)], [.. scenario.Publications.LastCommit!.VoteBills]);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Vote, vote, "Wrong vote.")] });
        Assert.Equal<ReleaseVoteBill>([new ReleaseVoteBill(other, action)], [.. scenario.Publications.LastCommit!.VoteBills]);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { Withdrawals = [new(PublishedRecordKind.Action, action, "Wrong bill.")] });
        Assert.Empty(scenario.Publications.LastCommit!.VoteBills);
    }

    private static PublishedRecordReference Reference(string id, PublishedRecordKind kind) => new(kind, id);
}
