using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Actions;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiSearchSourcesMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261112050000_PublicApiSearchSources";
    private static readonly DocumentExtraction[] Extractions = [.. Enumerable.Range(0, 11).Select(Extraction)];
    private static readonly string ActionId = new('a', 64);
    private static readonly string VoteId = new('b', 64);
    private static readonly string ExecutiveId = new('c', 64);
    private static readonly string CrsActionId = new('f', 64);
    private static readonly string RetiredActionId = new('d', 64);
    private static readonly string RetiredExecutiveId = new('e', 64);

    [Fact]
    public async Task EachSourceViewNumbersTheSourcesInTheOrderOfPublishedRecordRulesSourcesOverTheCitationsOfTheRevision()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
            await ViewsSeed.ActivateReleaseAsync(database, 2, DateTimeOffset.UnixEpoch.AddDays(1));
            await SeedExtractionsAsync(database);
            var action = ActionRevision(ActionId);
            var vote = VoteRevision();
            var executive = ExecutiveRevision(ExecutiveId);
            var crsAction = CrsSummaryActionRevision(CrsActionId);
            await WriteAsync(database, action, vote, executive);
            await WriteCrsActionAsync(database, crsAction);

            AssertOrderTellsTheRulesApart(action.Citations);
            Assert.NotEqual(crsAction.Citations.Select(span => span.ExtractionId).Order(StringComparer.Ordinal), crsAction.Citations.Select(span => span.ExtractionId));
            AssertOrderTellsTheRulesApart(vote.Citations);
            AssertOrderTellsTheRulesApart(executive.Citations);
            Assert.Equal(Expected(ActionId, action.Citations), await QueryAsPublicAsync(database, SourcesSql("action_sources", ActionId)));
            Assert.Equal(Expected(CrsActionId, crsAction.Citations), await QueryAsPublicAsync(database, SourcesSql("action_sources", CrsActionId)));
            Assert.Equal(Expected(VoteId, vote.Citations), await QueryAsPublicAsync(database, SourcesSql("vote_sources", VoteId)));
            Assert.Equal(Expected(ExecutiveId, executive.Citations), await QueryAsPublicAsync(database, SourcesSql("executive_action_sources", ExecutiveId)));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task OnlyTheActiveReleaseShowsSourcesAndARollbackMovesThemWhileThePublicRoleCannotReadTheCitationTables()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
            await ViewsSeed.ActivateReleaseAsync(database, 2, DateTimeOffset.UnixEpoch.AddDays(1));
            await SeedExtractionsAsync(database);
            await WriteAsync(database, ActionRevision(ActionId), VoteRevision(), ExecutiveRevision(ExecutiveId));
            await WriteRetiredAsync(database);

            Assert.Equal([ActionId], await QueryAsPublicAsync(database, "SELECT DISTINCT record_id FROM public_api.action_sources"));
            Assert.Equal([ExecutiveId], await QueryAsPublicAsync(database, "SELECT DISTINCT record_id FROM public_api.executive_action_sources"));

            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
            Assert.Equal([RetiredActionId], await QueryAsPublicAsync(database, "SELECT DISTINCT record_id FROM public_api.action_sources"));
            Assert.Equal([RetiredExecutiveId], await QueryAsPublicAsync(database, "SELECT DISTINCT record_id FROM public_api.executive_action_sources"));

            foreach (var table in new[] { "action_revision_sponsorship_citations", "action_revision_text_citations", "executive_action_revision_facts", "action_revisions", "relationship_revisions" })
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(() => QueryAsPublicAsync(database, $"SELECT record_id FROM public.{table}"));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
            }
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task AnExecutiveActionNoticeWithNoNoticeCitationKeepsItsSourceRowWithANullExtractionSoTheReaderRejectsTheRecord()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
            await ViewsSeed.ActivateReleaseAsync(database, 2, DateTimeOffset.UnixEpoch.AddDays(1));
            await SeedExtractionsAsync(database);
            var executive = ExecutiveRevision(ExecutiveId);
            await WriteAsync(database, ActionRevision(ActionId), VoteRevision(), executive);
            var uncited = executive.Notices[1].DocumentNumber;
            await ExecuteAsync(database, $"""
                UPDATE executive_action_documents SET revision_number = 1, notice_url = NULL, notice_citation_extraction_id = NULL, notice_citation_start = NULL, notice_citation_length = NULL
                 WHERE document_number = '{uncited}';
                """);

            var rows = await QueryAsPublicAsync(database, $"SELECT \"position\" || '|' || coalesce(extraction_id, 'null') || '|' || coalesce(url, 'null') FROM public_api.executive_action_sources WHERE record_id = '{ExecutiveId}' ORDER BY \"position\"");
            Assert.Equal(5, rows.Length);
            Assert.Equal("4|null|null", rows[^1]);
            Assert.DoesNotContain(rows[..^1], row => row.Contains("null", StringComparison.Ordinal));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task EachEndOfARelationshipShowsTheQuoteSpanOfTheExactPublishedRelationshipRevision()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            string link = new('1', 64), statement = new('2', 64), target = new('3', 64);
            await ExecuteAsync(database, $"""
                SET session_replication_role = replica;
                {PublicRecordPageRows.Link(link, statement, target, "action", "StatesPositionOn", "2026-02-01")}
                {PublicRecordPageRows.RelationshipRevision(link, 1, statement, target, "action", "StatesPositionOn", "Reason")}
                UPDATE relationship_revisions SET match_extraction_id = '{Extractions[2].ExtractionId}', match_start = 8, match_length = 2,
                    statement_quote_extraction_id = '{Extractions[2].ExtractionId}', statement_quote_start = 7, statement_quote_length = 4;
                {PublicRecordPageRows.Headline("statement", statement, "Statement headline")}
                {PublicRecordPageRows.Headline("action", target, "Action headline")}
                """);

            Assert.Equal([$"action|{Extractions[2].ExtractionId}|7|4", $"statement|{Extractions[2].ExtractionId}|7|4"], await QueryAsPublicAsync(database,
                "SELECT kind || '|' || quote_extraction_id || '|' || quote_start || '|' || quote_length FROM public_api.record_links ORDER BY kind"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task DowngradeRestoresTheOlderViewShapesAndRevokesOnlyTheGrantsThatNoOlderViewReads()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            var upgraded = await ColumnsAsync(database);
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);

            Assert.Equal([
                "record_links:record_id|kind|related_kind|related_record_id|related_headline|related_text_origin|related_label|link_type|statement_date|link_record_id|reason",
                "vote_sources:record_id|revision_number|extraction_id|url|observed_at_utc_ticks|text"
            ], await ColumnsAsync(database));
            Assert.Equal("0", await AddedGrantCountAsync(database));
            Assert.Equal(["true|true|true"], await QueryAsync(database, """
                SELECT has_column_privilege('civic_lens_public_owner', 'relationship_revisions', 'reason', 'SELECT')::text || '|'
                    || has_column_privilege('civic_lens_public_owner', 'executive_action_documents', 'notice_url', 'SELECT')::text || '|'
                    || has_column_privilege('civic_lens_public_owner', 'action_revisions', 'crs_summary_text', 'SELECT')::text
                """));
            Assert.Equal(["0", "0"], await QueryAsPublicAsync(database,
                "SELECT count(*)::text FROM public_api.vote_sources UNION ALL SELECT count(*)::text FROM public_api.record_links"));

            await db.GetService<IMigrator>().MigrateAsync();
            await ApplyGrantsAsync(database);
            Assert.Equal(upgraded, await ColumnsAsync(database));
            Assert.Equal("21", await AddedGrantCountAsync(database));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    // A test order is meaningful only when the first appearance differs from the extraction ID order of the older vote view
    // and from the order of the last appearance.
    private static void AssertOrderTellsTheRulesApart(IEnumerable<SourceSpan> citations)
    {
        var ids = citations.Select(span => span.ExtractionId).ToArray();
        var first = ids.Distinct(StringComparer.Ordinal).ToArray();
        Assert.NotEqual(first.Order(StringComparer.Ordinal), first);
        Assert.NotEqual(ids.Reverse().Distinct(StringComparer.Ordinal).Reverse(), first);
    }

    private static string[] Expected(string recordId, IEnumerable<SourceSpan> citations) =>
        [.. PublishedRecordRules.Sources(citations, Extractions.ToDictionary(extraction => extraction.ExtractionId, StringComparer.Ordinal))
            .Select((source, position) => string.Join('|', recordId, position, source.ExtractionId, source.Url, source.ObservedAtUtc.UtcTicks, source.Text))];

    private static string SourcesSql(string view, string recordId) =>
        $"SELECT record_id || '|' || \"position\" || '|' || extraction_id || '|' || url || '|' || observed_at_utc_ticks || '|' || text FROM public_api.{view} WHERE record_id = '{recordId}' ORDER BY \"position\"";

    private static DocumentExtraction Extraction(int index) => new(new CapturedAttemptResult(index.ToString("x32", CultureInfo.InvariantCulture), "search-test",
        $"https://requested.example/{index}", $"https://final.example/{index}", DateTimeOffset.UnixEpoch.AddMinutes(index + 1),
        new CollectionResponse(200, null, null, "text/html", []), new CaptureIdentity(new string('a', 64), 0)), "test", $"Source text {index}");

    private static Task SeedExtractionsAsync(string database) => ExecuteAsync(database, "SET session_replication_role = replica;\n" + string.Join('\n', Extractions.Select(extraction => $"""
        INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks,
            outcome, has_response, response_status_code, response_content_encodings, has_sent_validators, capture_sha256, elapsed_milliseconds)
        VALUES ('{extraction.SourceAttempt.AttemptId}', 'search-test', '{extraction.SourceAttempt.RequestedUrl}', '{extraction.SourceAttempt.FinalUrl}',
            {extraction.SourceAttempt.ObservedAt.UtcTicks}, 'captured', true, 200, ARRAY[]::text[], false, REPEAT('a', 64), 0);
        INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
        VALUES ('{extraction.ExtractionId}', '{extraction.SourceAttempt.AttemptId}', 'test', '{extraction.Text}', '{extraction.TextSha256}');
        """)));

    private static SourceSpan Cite(int index) => new(Extractions[index].ExtractionId, 0, 6);

    // Each group of the rank array holds extractions that no earlier group cites, so a swap of two groups, or of two rows inside one group, changes the order.
    // Sponsorship 0 cites 2 then 3, sponsorship 1 cites 1 then 2, the policy area cites 4, the bill cites 6, and the text cites 0, 8, and 7.
    // A swap of the two sponsorship keys gives 2, 1, 3.
    private static ActionDraftRevision ActionRevision(string recordId) => ActionRevisionFixtures.TextModel() with
    {
        RecordId = recordId,
        Sponsorships = [ActionRevisionFixtures.Sponsorship() with { Citations = [Cite(2), Cite(3)] },
            ActionRevisionFixtures.Sponsorship("S000001") with { PersonId = "sanders", Role = ActionSponsorshipRole.Cosponsor, Citations = [Cite(1), Cite(2)] }],
        SummaryVersion = null,
        SummaryCitation = null,
        PolicyArea = "Housing",
        PolicyAreaId = "housing",
        PolicyAreaCitation = Cite(4),
        BillCitation = Cite(6),
        TextCitations = [new(ActionTextPart.Summary, Cite(0), "First sentence."), new(ActionTextPart.Summary, Cite(8), "Second sentence."),
            new(ActionTextPart.WhyItMatters, Cite(7), "Third sentence.")]
    };

    // An action cites the CRS summary or the bill text, never both, so this revision tests the summary group. It cites 9, then 5, 10, and 6.
    private static ActionDraftRevision CrsSummaryActionRevision(string recordId) => ActionRevisionFixtures.Model() with
    {
        RecordId = recordId,
        Sponsorships = [ActionRevisionFixtures.Sponsorship() with { Citations = [Cite(9)] }],
        SummaryVersion = "Introduced",
        SummaryCitation = Cite(5),
        PolicyArea = "Housing",
        PolicyAreaId = "housing",
        PolicyAreaCitation = Cite(10),
        BillCitation = Cite(6)
    };

    private static VoteDraftRevision VoteRevision() => new(VoteId, 1, "auth0|editor", DateTimeOffset.UnixEpoch, "Vote headline", "Vote summary",
        ReviewTextOrigin.Person, null, "congress-house-votes", VoteSource.CongressGov, Chamber.House, 119, 1, 100, null, null, new string('f', 64),
        DateTimeOffset.UnixEpoch, [new("Question: On Passage", Cite(1)), new("Result: Passed", Cite(6))], new DateOnly(2026, 1, 2), Cite(3),
        VoteKind.Passage, Cite(0), [new("m1", "M000001", "Yea", Cite(4)), new("m2", "M000002", "Nay", Cite(1)), new("m3", "M000003", "Yea", Cite(2))],
        [], [], new string('e', 64));

    private static ExecutiveActionRevision ExecutiveRevision(string recordId) => new(recordId, 1, ReviewAuthor.CodeSubject, DateTimeOffset.UnixEpoch,
        "fr", $"2026-{recordId[..5]}", ExecutiveActionKind.ExecutiveOrder, "14000", "Title", new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 23), "91 FR 1",
        "Jane Roe", "president", "https://fr.example/1", [new("Title", "Title", Cite(4)), new("Signed", "January 20, 2026", Cite(2)), new("Published", "January 23, 2026", Cite(4))],
        [new($"2026-{recordId[..4]}9", new DateOnly(2026, 2, 1), "https://fr.example/9", Cite(1)), new($"2026-{recordId[..4]}8", new DateOnly(2026, 2, 2), "https://fr.example/8", Cite(5))])
    {
        DispositionNotes = new(ExecutiveActionFacts.DispositionNotes, "Revokes: EO 1", Cite(0))
    };

    // The action and the executive action are active from release 2. Their retired twins are active only in release 1.
    private static async Task WriteAsync(string database, ActionDraftRevision action, VoteDraftRevision vote, ExecutiveActionRevision executive)
    {
        await ActionRevisionSeeder.AddAsync(database, action);
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
        await db.Database.OpenConnectionAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
        db.Add(InternalMethods.Invoke("CivicLens.Infrastructure.Votes.VoteRevisionRows", "Create", vote, null));
        await InternalMethods.InvokeAsync("CivicLens.Infrastructure.Votes.VoteMemberLists", "EnsureAsync", db, vote, CancellationToken.None);
        db.Add(InternalMethods.Invoke("CivicLens.Infrastructure.ExecutiveActions.ExecutiveActionRevisionRows", "Create", executive, TestCodeVersion.Current));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await ExecuteAsync(database, $"""
            {Notices(executive)}
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('action', '{action.RecordId}', 2, 1, NULL), ('vote', '{vote.RecordId}', 1, 1, NULL), ('executiveAction', '{executive.RecordId}', 2, 1, NULL);
            """);
    }

    private static async Task WriteCrsActionAsync(string database, ActionDraftRevision action)
    {
        await ActionRevisionSeeder.AddAsync(database, action);
        await ExecuteAsync(database, $"INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('action', '{action.RecordId}', 2, 1, NULL);");
    }

    private static async Task WriteRetiredAsync(string database)
    {
        await ActionRevisionSeeder.AddAsync(database, ActionRevision(RetiredActionId));
        var executive = ExecutiveRevision(RetiredExecutiveId);
        await using (var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options))
        {
            db.Add(InternalMethods.Invoke("CivicLens.Infrastructure.ExecutiveActions.ExecutiveActionRevisionRows", "Create", executive, TestCodeVersion.Current));
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
            await db.SaveChangesAsync();
        }
        await ExecuteAsync(database, $"""
            {Notices(executive)}
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('action', '{RetiredActionId}', 1, 1, 1), ('executiveAction', '{RetiredExecutiveId}', 1, 1, 1);
            """);
    }

    private static string Notices(ExecutiveActionRevision revision) => "SET session_replication_role = replica;\n" + string.Join('\n', revision.Notices.Select(notice => $"""
        INSERT INTO executive_action_documents (source_id, document_number, record_id, revision_number, published_on, document_job_id, notice_url,
            notice_citation_extraction_id, notice_citation_start, notice_citation_length)
          VALUES ('{revision.SourceId}', '{notice.DocumentNumber}', '{revision.RecordId}', NULL, '{notice.PublishedOn:yyyy-MM-dd}', 'job', '{notice.Url}',
            '{notice.Citation.ExtractionId}', {notice.Citation.Start}, {notice.Citation.Length});
        """));

    private static Task<string[]> ColumnsAsync(string database) => QueryAsync(database, """
        SELECT c.relname || ':' || string_agg(a.attname, '|' ORDER BY a.attnum)
          FROM pg_class c
          JOIN pg_namespace n ON n.oid = c.relnamespace
          JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
         WHERE n.nspname = 'public_api' AND c.relname IN ('vote_sources', 'record_links', 'action_sources', 'executive_action_sources')
         GROUP BY c.relname ORDER BY c.relname
        """);

    private static async Task<string> AddedGrantCountAsync(string database) => (await QueryAsync(database, """
        SELECT count(*)::text FROM (VALUES
            ('action_revisions', 'summary_citation_extraction_id'), ('action_revisions', 'policy_area_citation_extraction_id'),
            ('action_revisions', 'bill_citation_extraction_id'),
            ('action_revision_sponsorship_citations', 'record_id'), ('action_revision_sponsorship_citations', 'revision_number'),
            ('action_revision_sponsorship_citations', 'sponsorship_position'), ('action_revision_sponsorship_citations', 'position'),
            ('action_revision_sponsorship_citations', 'citation_extraction_id'),
            ('action_revision_text_citations', 'record_id'), ('action_revision_text_citations', 'revision_number'),
            ('action_revision_text_citations', 'position'), ('action_revision_text_citations', 'citation_extraction_id'),
            ('executive_action_revision_facts', 'record_id'), ('executive_action_revision_facts', 'revision_number'),
            ('executive_action_revision_facts', 'position'), ('executive_action_revision_facts', 'citation_extraction_id'),
            ('executive_action_revisions', 'disposition_notes_extraction_id'),
            ('executive_action_documents', 'notice_citation_extraction_id'),
            ('relationship_revisions', 'statement_quote_extraction_id'), ('relationship_revisions', 'statement_quote_start'),
            ('relationship_revisions', 'statement_quote_length')
        ) added(table_name, column_name)
        WHERE has_column_privilege('civic_lens_public_owner', added.table_name, added.column_name, 'SELECT')
        """))[0];

}
