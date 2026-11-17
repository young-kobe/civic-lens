using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiVoteEvidenceViewsTests(PostgresCollection postgres)
{
    private static readonly string DateSource = new('1', 64);
    private static readonly string FactSource = new('2', 64);
    private static readonly string KindSource = new('3', 64);
    private static readonly string MemberSource = new('4', 64);
    private static readonly string MissingExtraction = new('5', 64);
    private static readonly string OrphanSource = new('6', 64);
    private static readonly string OldSource = new('7', 64);

    [Fact]
    public async Task TheVoteSourceAndMemberViewsShowOnlyTheEvidenceOfTheActiveRevisionOfAPublishedRecord()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
            await ViewsSeed.ActivateReleaseAsync(database, 2, DateTimeOffset.UnixEpoch.AddDays(1));
            var active = new SeededVote('a', "2026-01-01") { ActiveRevision = 2, PersonIds = ["member-a", "member-b", "member-c"] };
            var retired = new SeededVote('b', "2026-01-02") { LastRelease = 1 };
            var future = new SeededVote('c', "2026-01-03") { FirstRelease = 3 };
            await ViewsSeed.AddVotesAsync(database, active, retired, future);
            await ViewsSeed.ExecuteAsync(database, $"""
                BEGIN;
                SET LOCAL session_replication_role = replica;
                UPDATE vote_revisions SET voted_on_citation_start = 0, voted_on_citation_length = 1,
                    kind_citation_start = 0, kind_citation_length = 1,
                    voted_on_citation_extraction_id = CASE WHEN revision_number = 2 THEN '{DateSource}' ELSE '{OldSource}' END,
                    kind_citation_extraction_id = CASE WHEN revision_number = 2 THEN '{KindSource}' ELSE '{OldSource}' END
                 WHERE record_id = '{active.RecordId}';
                UPDATE vote_revision_facts SET citation_start = 0, citation_length = 1, citation_extraction_id = CASE
                    WHEN revision_number = 2 AND position = 0 THEN '{FactSource}'
                    WHEN revision_number = 2 THEN '{MissingExtraction}'
                    ELSE '{OldSource}' END
                 WHERE record_id = '{active.RecordId}';
                UPDATE vote_member_list_entries SET citation_start = 0, citation_length = 1, citation_extraction_id = CASE person_id
                    WHEN 'member-a' THEN '{MemberSource}'
                    WHEN 'member-b' THEN '{FactSource}'
                    ELSE '{MemberSource}' END
                 WHERE list_id = '{ViewsSeed.MemberListId(active.RecordId, active.ActiveRevision)}';
                INSERT INTO vote_revision_facts (record_id, revision_number, "position", fact_text, citation_extraction_id, citation_start, citation_length)
                VALUES ('{active.RecordId}', 2, 2, 'Member roster fact', '{OrphanSource}', 0, 1);
                INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks,
                    outcome, has_response, response_status_code, response_content_encodings, has_sent_validators,
                    capture_sha256, elapsed_milliseconds)
                VALUES ('vote-attempt', 'source', 'https://requested.example/vote', 'https://final.example/vote', 987654,
                    'captured', true, 200, ARRAY[]::text[], false, REPEAT('a', 64), 0);
                INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
                VALUES ('{DateSource}', 'vote-attempt', 'test', 'Date evidence text', REPEAT('b', 64)),
                       ('{KindSource}', 'vote-attempt', 'test', 'Kind evidence text', REPEAT('c', 64)),
                       ('{FactSource}', 'vote-attempt', 'test', 'Fact evidence text', REPEAT('d', 64)),
                       ('{MemberSource}', 'vote-attempt', 'test', 'Member evidence text', REPEAT('e', 64)),
                       ('{OldSource}', 'vote-attempt', 'test', 'Old revision evidence', REPEAT('f', 64)),
                       ('{OrphanSource}', 'missing-attempt', 'test', 'Evidence without attempt metadata', REPEAT('1', 64));
                COMMIT;
                """);

            Assert.Equal([
                $"{active.RecordId}|2|{DateSource}|https://final.example/vote|987654|Date evidence text",
                $"{active.RecordId}|2|{FactSource}|https://final.example/vote|987654|Fact evidence text",
                $"{active.RecordId}|2|{KindSource}|https://final.example/vote|987654|Kind evidence text",
                $"{active.RecordId}|2|{MemberSource}|https://final.example/vote|987654|Member evidence text",
                $"{active.RecordId}|2|{MissingExtraction}|||",
                $"{active.RecordId}|2|{OrphanSource}|||Evidence without attempt metadata"
            ], await QueryAsPublicAsync(database, """
                SELECT record_id || '|' || revision_number || '|' || extraction_id || '|' || coalesce(url, '') || '|'
                    || coalesce(observed_at_utc_ticks::text, '') || '|' || coalesce(text, '')
                  FROM public_api.vote_sources ORDER BY extraction_id
                """));
            Assert.Equal([$"{active.RecordId}|2|member-a|{MemberSource}|0|1", $"{active.RecordId}|2|member-b|{FactSource}|0|1", $"{active.RecordId}|2|member-c|{MemberSource}|0|1"],
                await QueryAsPublicAsync(database, $"SELECT record_id || '|' || revision_number || '|' || official_id || '|' || extraction_id || '|' || citation_start || '|' || citation_length FROM public_api.vote_members WHERE record_id = '{active.RecordId}' ORDER BY official_id"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }
}
