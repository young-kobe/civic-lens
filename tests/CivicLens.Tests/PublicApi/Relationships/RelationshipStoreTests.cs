using CivicLens.Application.Relationships;
using CivicLens.PublicApi.Records;
using CivicLens.PublicApi.Relationships;
using CivicLens.PublicApi.Statements;
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
    public async Task AModelReasonCarriesTheRelationshipLabelAndAPersonReasonCarriesNone()
    {
        var model = (await FindAsync(BillLink)).Page;
        var person = (await FindAsync(VoteLink)).Page;

        Assert.Equal((PublishedTextOrigin.Model, PublishedWording.ModelRelationship), (model.TextOrigin, model.Label));
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
            Assert.Equal(fact.Id is "F3" or "F4" ? (AiLabelWords.ModelWording, PublishedTextOrigin.Model) : (null, null), (page.FactLabel, page.FactTextOrigin));
        }
    }

    [Fact]
    public async Task ACodeFactOfABillCarriesTheCodeOriginBesideTheCodeLabel()
    {
        await ViewsSeed.ExecuteAsync(database, $"SET session_replication_role = replica; UPDATE action_revisions SET text_origin = 'Code' WHERE record_id = '{ActionId}'");

        var page = (await FindAsync(BillLink)).Page;

        Assert.Equal((AiLabelWords.CodeWording, PublishedTextOrigin.Code), (page.FactLabel, page.FactTextOrigin));
    }

    [Fact]
    public async Task AVoteFactIsTheNumberedFactInPositionOrderWithNoLabel()
    {
        var page = (await FindAsync(VoteLink)).Page;

        Assert.Equal(VoteFactTexts[1], page.Fact);
        Assert.Equal((null, null), (page.FactLabel, page.FactTextOrigin));
        Assert.Equal((PublishedRecordKind.Vote, VoteId, 1, "Vote on housing"), (page.Target.Kind, page.Target.RecordId, page.Target.RevisionNumber, page.Target.Headline));
    }

    [Fact]
    public async Task TheRestrictedRelationshipsViewReturnsTheSavedCastAndDateFromTheCitedVoteRevision()
    {
        var newerVoteId = new string('2', 64);
        await ViewsSeed.AddVotesAsync(database, new SeededVote('2', "2026-05-10")
        {
            ActiveRevision = 2,
            Casts = ["Nay", "Yea"],
            Result = "Passed 218 to 210",
            PersonIds = ["official", "other"]
        });
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            UPDATE vote_revisions SET headline = 'Current vote title', text_origin = 'Person', voted_on = '2026-05-11'
             WHERE record_id = '{newerVoteId}' AND revision_number = 2;
            UPDATE vote_member_list_entries SET vote_cast = 'Yea'
             WHERE list_id = (SELECT member_list_id FROM vote_revisions WHERE record_id = '{newerVoteId}' AND revision_number = 2) AND person_id = 'official';
            UPDATE vote_member_lists SET yea_count = 2, nay_count = 0
             WHERE list_id = (SELECT member_list_id FROM vote_revisions WHERE record_id = '{newerVoteId}' AND revision_number = 2);
            UPDATE vote_revision_facts SET fact_text = 'Result: Changed'
             WHERE record_id = '{newerVoteId}' AND revision_number = 2 AND "position" = 1;
            UPDATE relationship_revisions SET target_vote_record_id = '{newerVoteId}', target_vote_revision_number = 1, target_fact_id = 'F2'
             WHERE record_id = '{VoteLink}';
            UPDATE published_entries SET target_record_id = '{newerVoteId}'
             WHERE kind = 'relationship' AND record_id = '{VoteLink}';
            """);

        await using (var deniedConnection = await views.OpenConnectionAsync())
        await using (var denied = new NpgsqlCommand("SELECT official_id FROM public.relationship_revisions", deniedConnection))
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteReaderAsync());
            Assert.Equal("42501", error.SqlState);
        }

        await using (var active = await views.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand($"SELECT v.voted_on, m.vote_cast FROM public_api.votes v JOIN public_api.member_votes m ON m.record_id = v.record_id WHERE v.record_id = '{newerVoteId}' AND m.official_id = 'official'", active))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(new DateOnly(2026, 5, 11), reader.GetFieldValue<DateOnly>(0));
            Assert.Equal("Yea", reader.GetString(1));
        }

        var (connection, transaction) = await new RecordSnapshot(views).OpenAsync(CancellationToken.None);
        await using var _ = connection;
        await using var __ = transaction;
        var page = await new StatementLinkedVoteReader().ReadAsync(transaction, StatementId, new(), CancellationToken.None);
        var linked = Assert.Single(Assert.IsType<StatementLinkedVotePage>(page).Votes);
        Assert.Equal(1, linked.CitedStatementRevisionNumber);
        Assert.Equal((newerVoteId, 1, "Nay", new DateOnly(2026, 5, 10)),
            (linked.VoteRecordId, linked.CitedVoteRevisionNumber, linked.CitedVoteCast, linked.VotedOn));
        Assert.Equal((PublishedRelationshipType.StatementAboutVote, Reason, Quote, "Result: Passed 218 to 210"),
            (linked.RelationshipType, linked.Reason, linked.Quote, linked.Fact));
        Assert.Equal(("Current vote title", PublishedTextOrigin.Person, (string?)null),
            (linked.VoteHeadline, linked.VoteTextOrigin, linked.VoteLabel));
    }

    [Fact]
    public async Task LinkedVotesPageTenNewestAndCanWalkBackwardWithoutRepeats()
    {
        char[] ids = ['0', '2', '3', '4', '6', '9', 'b', 'c', 'd', 'e', 'f'];
        var votes = ids.Select(id => new SeededVote(id, "2026-06-11")
        {
            Casts = ["Yea"],
            PersonIds = ["official"]
        }).ToArray();
        await ViewsSeed.AddVotesAsync(database, votes);
        await ViewsSeed.ExecuteAsync(database, "SET session_replication_role = replica;\n" + string.Join('\n', votes.Select(vote =>
        {
            var link = new string(vote.Id, 64);
            return $"""
                {PublicRecordPageRows.Link(link, StatementId, vote.RecordId, "vote", "StatementAboutVote", null)}
                {PublicRecordPageRows.RelationshipRevision(link, 1, StatementId, vote.RecordId, "vote", "StatementAboutVote", Reason)}
                UPDATE relationship_revisions SET statement_quote_extraction_id = '{ExtractionId}', statement_quote_start = {QuoteStart},
                    statement_quote_length = {Quote.Length}, match_extraction_id = '{ExtractionId}', match_start = {QuoteStart + 1}, match_length = 4,
                    target_fact_id = 'F2' WHERE record_id = '{link}';
                """;
        })));

        var (connection, transaction) = await new RecordSnapshot(views).OpenAsync(CancellationToken.None);
        await using var _ = connection;
        await using var __ = transaction;
        var reader = new StatementLinkedVoteReader();
        var first = Assert.IsType<StatementLinkedVotePage>(await reader.ReadAsync(transaction, StatementId, new(), CancellationToken.None));
        Assert.Equal(StatementLinkedVoteRequest.DefaultLimit, first.Votes.Length);
        Assert.Null(first.PreviousCursor);
        Assert.NotNull(first.NextCursor);
        var second = Assert.IsType<StatementLinkedVotePage>(await reader.ReadAsync(transaction, StatementId,
            new() { Cursor = StatementLinkedVoteCursor.Parse(first.NextCursor) }, CancellationToken.None));
        Assert.Equal(2, second.Votes.Length);
        Assert.Null(second.NextCursor);
        Assert.NotNull(second.PreviousCursor);
        var previous = Assert.IsType<StatementLinkedVotePage>(await reader.ReadAsync(transaction, StatementId,
            new() { Cursor = StatementLinkedVoteCursor.Parse(second.PreviousCursor), Backward = true }, CancellationToken.None));
        Assert.Equal(first.Votes.Select(vote => vote.VoteRecordId), previous.Votes.Select(vote => vote.VoteRecordId));
        Assert.Equal(votes.Select(vote => vote.RecordId).Append(VoteId), first.Votes.Select(vote => vote.VoteRecordId).Concat(second.Votes.Select(vote => vote.VoteRecordId)));
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
        Assert.Equal(AiLabelWords.ModelWording, page.FactLabel);
        Assert.Equal((1, "Housing bill, revised", PublishedTextOrigin.Person, (string?)null),
            (page.Target.RevisionNumber, page.Target.Headline, page.Target.TextOrigin, page.Target.Label));
        Assert.Equal((PublishedRecordKind.Statement, StatementId, 1, PublishedTextOrigin.Model, AiLabelWords.StatementCheckedByPersonWording),
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
