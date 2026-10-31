using CivicLens.Application.Actions;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReviewListTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Housing = "housing";
    private const string Health = "health";
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private string connectionString = null!;
    private RelationshipWorldSeeder seeder = null!;
    private PostgresReviewQueueStore queue = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        seeder = new RelationshipWorldSeeder(connectionString);
        queue = new PostgresReviewQueueStore(AnalysisDbContextFactory.Create(connectionString));
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ANewRevisionOfAnOldActionMovesItAboveNewerRecords()
    {
        await SeedActionAsync('1', 11, revisedAt: 100);
        await SeedActionAsync('2', 12, revisedAt: 200);
        await SeedActionAsync('3', 13, revisedAt: 300);

        await new SaveActionDraft(PostgresActionReviewStore.FromConnectionString(connectionString)).ExecuteAsync(Reviewer,
            new(Id('1'), 1, "Person headline", "Person summary.", SuggestionChoice.Keep, "save-2"));

        var page = await queue.ListAsync(null, 10, default);
        Assert.Equal(Ids('1', '3', '2'), page.Items.Select(item => item.RecordId));
    }

    [Fact]
    public async Task TheQueueRefusesARankedCursorSoACursorFromAnotherListCannotMovePastItsRows()
    {
        var ranked = PageCursor.Parse("o.1.5." + Id('1'));

        await Assert.ThrowsAsync<ArgumentException>(() => queue.ListAsync(ranked, 1, default));
    }

    [Fact]
    public async Task AnUnmatchedApprovedLinkThatIsAlsoStuckIsLeftOutOfTheRowsAndTheTotalSoItShowsOnce()
    {
        await seeder.StatementAsync(RelationshipFixtures.Statement());
        var link = RelationshipFixtures.Revision();
        var other = link with { RecordId = Id('2') };
        await seeder.RevisionAsync(link);
        await seeder.RevisionAsync(other);
        await seeder.ExecuteAsync("UPDATE relationship_drafts SET review_status = 'Approved', no_longer_matches = true");

        var both = await queue.ListUnmatchedApprovedLinksAsync(10, [], default);
        var withoutStuck = await queue.ListUnmatchedApprovedLinksAsync(10, [link.RecordId], default);

        Assert.Equal(2, both.Total);
        Assert.Equal(1, withoutStuck.Total);
        Assert.Equal([other.RecordId], withoutStuck.Items.Select(item => item.RecordId));
    }

    private static string Id(char digit) => new(digit, 64);

    private static string[] Ids(params char[] digits) => [.. digits.Select(Id)];

    private async Task SeedActionAsync(char id, int billNumber, long revisedAt, string status = "AwaitingReview")
    {
        var action = RelationshipFixtures.Action(billNumber) with { RecordId = Id(id), CandidateId = Id(id) };
        await seeder.ActionAsync(action, status, revisedAt);
    }

    private async Task SeedVoteAsync(char id, int rollCall, long revisedAt, string status = "AwaitingReview")
    {
        var vote = RelationshipFixtures.Vote() with { RecordId = Id(id), CandidateId = Id(id), RollCallNumber = rollCall };
        await seeder.VoteAsync(vote, status, revisedAt);
    }
}
