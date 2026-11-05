using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class ReleaseBillLinesTests
{
    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AVoteTakesTheLineOfTheFirstActionOnItsBillThatTheReleaseHoldsBecauseThatIsTheActionItsPageLinks()
    {
        var held = Entry(PublishedRecordKind.Action, 'b') with { BillLine = "This bill funds rent aid." };
        var vote = Entry(PublishedRecordKind.Vote, 'v');

        var lines = ReleaseBillLines.From([held, vote], new Dictionary<string, ImmutableArray<string>> { [vote.RecordId] = [Id('a'), held.RecordId] });

        Assert.Equal(new VoteBillLink(held.RecordId, "This bill funds rent aid."), lines.BillOfVote(vote.RecordId));
        Assert.Equal("This bill funds rent aid.", lines.Of(vote));
        Assert.Equal("This bill funds rent aid.", lines.Of(held));
    }

    [Fact]
    public void AVoteWhoseActionIsNotInTheReleaseHasNoBillAndAVoteWhoseActionHoldsNoLineLinksTheActionWithNoLine()
    {
        var bare = Entry(PublishedRecordKind.Action, 'b');
        var gone = Entry(PublishedRecordKind.Vote, 'v');
        var linked = Entry(PublishedRecordKind.Vote, 'w');

        var lines = ReleaseBillLines.From([bare, gone, linked], new Dictionary<string, ImmutableArray<string>>
        {
            [gone.RecordId] = [Id('a')],
            [linked.RecordId] = [bare.RecordId]
        });

        Assert.Null(lines.BillOfVote(gone.RecordId));
        Assert.Equal(new VoteBillLink(bare.RecordId, null), lines.BillOfVote(linked.RecordId));
        Assert.False(lines.AnyOf([gone, linked, bare]));
    }

    [Fact]
    public void ARecordThatIsNeitherAnActionNorAVoteHasNoLine()
    {
        Assert.Null(ReleaseBillLines.None.Of(Entry(PublishedRecordKind.Statement, 's')));
    }

    private static string Id(char letter) => new(letter, 64);

    private static PublishedRecordEntry Entry(PublishedRecordKind kind, char id) => new()
    {
        Kind = kind,
        RecordId = Id(id),
        RevisionNumber = 1,
        Headline = $"Headline {id}",
        FirstPublishedAtUtc = Published,
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = []
    };
}
