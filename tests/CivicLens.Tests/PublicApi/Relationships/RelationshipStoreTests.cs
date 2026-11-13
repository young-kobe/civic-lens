using CivicLens.Application.Relationships;
using CivicLens.PublicApi.Records;
using CivicLens.PublicApi.Relationships;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;
using static CivicLens.Tests.PublicApi.Relationships.RelationshipSeed;

namespace CivicLens.Tests.PublicApi.Relationships;

/// <summary>The relationship store reads <c>public_api.relationships</c> as the public role. Each text keeps its exact characters.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RelationshipStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Password = "relationship-test";
    private string database = null!;
    private NpgsqlDataSource views = null!;

    public async Task InitializeAsync()
    {
        database = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ExecuteAsync(database, $"ALTER ROLE civic_lens_public LOGIN PASSWORD '{Password}'");
        views = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(database) { Username = "civic_lens_public", Password = Password }.ConnectionString);
        await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await SeedAsync(database);
    }

    // The role belongs to the cluster, so each test puts it back to the state that the migration leaves.
    public async Task DisposeAsync()
    {
        await views.DisposeAsync();
        await ViewsSeed.ExecuteAsync(database, "ALTER ROLE civic_lens_public NOLOGIN PASSWORD NULL");
        await postgres.DropDatabaseAsync(database);
    }

    [Fact]
    public async Task TheQuoteTheReasonAndTheFactKeepTheirExactCharactersBecauseTheQuoteSpanCountsUtf16Units()
    {
        var detail = await FindAsync(BillLink);

        var page = detail.Page;
        // The text before the quote holds characters of two UTF-16 units, so an offset in code points would point elsewhere.
        Assert.NotEqual(QuoteStart, Source[..QuoteStart].EnumerateRunes().Count());
        Assert.Equal(Quote, page.Quote);
        Assert.Equal(Reason, page.Reason);
        Assert.Equal($"Summary: {ActionSummary}", page.Fact);
        Assert.Equal("Doe on rent and Housing bill", page.Headline);
        Assert.Equal(new DateOnly(2026, 5, 1), page.StatementDate);
        Assert.Equal(new DateTimeOffset(2026, 5, 2, 14, 0, 0, TimeSpan.Zero), page.ApprovedAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 5, 2, 14, 5, 0, TimeSpan.Zero), page.FirstPublishedAtUtc);
        Assert.Equal(PublishedRelationshipType.DescribesOwnBill, page.Type);
        Assert.Equal(["official-z", "official-a"], detail.OfficialIds);
        Assert.Equal(["housing"], detail.IssueIds);
    }

    [Fact]
    public async Task AModelReasonCarriesTheLabelOfPersonApprovedModelTextAndAPersonReasonCarriesNone()
    {
        var model = (await FindAsync(BillLink)).Page;
        var person = (await FindAsync(VoteLink)).Page;

        Assert.Equal((PublishedTextOrigin.Model, PublishedRecordLabels.StatementCheckedByPersonWording), (model.TextOrigin, model.Label));
        Assert.Equal((PublishedTextOrigin.Person, (string?)null), (person.TextOrigin, person.Label));
        Assert.Null(person.StatementDate);
    }

    [Fact]
    public async Task EachFactOfABillIsTheFactThatTheRelationshipTargetRuleNumbersAndTheModelTextFactsCarryTheAiLabel()
    {
        // The seeded bill row holds these values. The C# rule that the draft prompt and the published file use is the oracle.
        var target = RelationshipTarget.Of(ActionRevisionFixtures.Model() with
        {
            RecordId = ActionId,
            Congress = 119,
            BillType = "hr",
            BillNumber = 7,
            BillTitle = "A housing bill",
            Headline = ActionHeadline,
            Summary = ActionSummary
        });
        Assert.Equal(4, target.Facts.Length);

        foreach (var fact in target.Facts)
        {
            await ViewsSeed.ExecuteAsync(database, $"UPDATE relationship_revisions SET target_fact_id = '{fact.Id}' WHERE record_id = '{BillLink}'");
            var page = (await FindAsync(BillLink)).Page;

            Assert.Equal(fact.Text, page.Fact);
            Assert.Equal(fact.Id is "F3" or "F4" ? PublishedRecordLabels.ModelWording : null, page.FactLabel);
        }
    }

    [Fact]
    public async Task AVoteFactIsTheNumberedFactInPositionOrderWithNoLabel()
    {
        var page = (await FindAsync(VoteLink)).Page;

        Assert.Equal(VoteFactTexts[1], page.Fact);
        Assert.Null(page.FactLabel);
        Assert.Equal((PublishedRecordKind.Vote, VoteId, 1, "Vote on housing"), (page.Target.Kind, page.Target.RecordId, page.Target.RevisionNumber, page.Target.Headline));
    }

    [Fact]
    public async Task TheFactComesFromTheCitedRevisionAndTheEndShowsTheCurrentHeadlineSoANewerTargetRevisionNeverChangesTheEvidence()
    {
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            {PublicActionRows.ActionRevision(ActionId, 2, "Person", null, null, headline: "Housing bill, revised")}
            UPDATE publication_record_states SET revision_number = 2 WHERE kind = 'action' AND record_id = '{ActionId}';
            """);

        var page = (await FindAsync(BillLink)).Page;

        Assert.Equal($"Summary: {ActionSummary}", page.Fact);
        Assert.Equal(PublishedRecordLabels.ModelWording, page.FactLabel);
        Assert.Equal((1, "Housing bill, revised", PublishedTextOrigin.Person, (string?)null),
            (page.Target.RevisionNumber, page.Target.Headline, page.Target.TextOrigin, page.Target.Label));
        Assert.Equal((PublishedRecordKind.Statement, StatementId, 1, PublishedTextOrigin.Model, PublishedRecordLabels.StatementCheckedByPersonWording),
            (page.Statement.Kind, page.Statement.RecordId, page.Statement.RevisionNumber, page.Statement.TextOrigin, page.Statement.Label));
    }

    [Fact]
    public async Task ARelationshipWhoseTargetIsNotActiveHasNoPageBecauseAWithdrawnRecordNeverLinks()
    {
        await ViewsSeed.ExecuteAsync(database, $"SET session_replication_role = replica; UPDATE publication_record_states SET first_release = 2 WHERE kind = 'vote' AND record_id = '{VoteId}'");

        await using var connection = await views.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var (release, detail) = await new RelationshipStore().FindAsync(transaction, VoteLink, CancellationToken.None);

        Assert.Equal(1, release);
        Assert.Null(detail);
        Assert.NotNull((await new RelationshipStore().FindAsync(transaction, BillLink, CancellationToken.None)).Detail);
    }

    private async Task<RelationshipDetail> FindAsync(string recordId)
    {
        var (connection, transaction) = await new RecordSnapshot(views).OpenAsync(CancellationToken.None);
        await using var _ = connection;
        await using var __ = transaction;
        return Assert.IsType<RelationshipDetail>((await new RelationshipStore().FindAsync(transaction, recordId, CancellationToken.None)).Detail);
    }
}
