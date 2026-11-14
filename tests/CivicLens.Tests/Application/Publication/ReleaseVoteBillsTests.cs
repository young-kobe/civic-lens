using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class ReleaseVoteBillsTests
{
    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AVoteTakesTheFirstActionOnItsBillThatTheReleaseHolds()
    {
        var held = Entry(PublishedRecordKind.Action, 'b');
        var vote = Entry(PublishedRecordKind.Vote, 'v');

        var bills = ReleaseVoteBills.From([held, vote], new Dictionary<string, ImmutableArray<string>> { [vote.RecordId] = [Id('a'), held.RecordId] });

        Assert.Equal(new ReleaseVoteBill(vote.RecordId, held.RecordId), Assert.Single(bills));
    }

    [Fact]
    public void AVoteWhoseActionIsNotInTheReleaseHasNoBill()
    {
        var gone = Entry(PublishedRecordKind.Vote, 'v');

        var bills = ReleaseVoteBills.From([gone], new Dictionary<string, ImmutableArray<string>> { [gone.RecordId] = [Id('a')] });

        Assert.Empty(bills);
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
