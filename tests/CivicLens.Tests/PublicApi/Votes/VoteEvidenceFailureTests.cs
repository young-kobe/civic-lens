using System.Net;

namespace CivicLens.Tests.PublicApi.Votes;

public sealed partial class VoteEndpointsTests
{
    [Theory]
    [InlineData("missing-member-extraction")]
    [InlineData("missing-member-attempt")]
    [InlineData("invalid-member-url")]
    [InlineData("member-citation-out-of-range")]
    [InlineData("member-cast-mismatch")]
    public async Task InvalidMemberEvidenceFailsClosedOnDetailAndMemberRoutes(string corruption)
    {
        var vote = new SeededVote('a', "2026-03-02");
        await using var host = await StartWithVotesAsync(vote);
        var memberSource = ViewsSeed.ExtractionId(vote.RecordId, 1, "members");
        var memberList = ViewsSeed.MemberListId(vote.RecordId, 1);
        var sql = corruption switch
        {
            "missing-member-extraction" => $"DELETE FROM document_extractions WHERE extraction_id = '{memberSource}';",
            "missing-member-attempt" => $"UPDATE document_extractions SET attempt_id = 'missing-attempt' WHERE extraction_id = '{memberSource}';",
            "invalid-member-url" => $"UPDATE collection_attempts SET final_url = 'not-a-web-url' WHERE attempt_id = 'attempt-{memberSource}';",
            "member-citation-out-of-range" => $"UPDATE vote_member_list_entries SET citation_start = 100000 WHERE list_id = '{memberList}' AND position = 0;",
            "member-cast-mismatch" => $"UPDATE vote_member_list_entries SET vote_cast = 'Nay' WHERE list_id = '{memberList}' AND position = 0;",
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "BEGIN; SET LOCAL session_replication_role = replica; " + sql + " COMMIT;");

        foreach (var path in new[] { $"/v1/votes/{vote.RecordId}", $"/v1/votes/{vote.RecordId}/members" })
        {
            using var response = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("fact-text-mismatch")]
    [InlineData("missing-result-fact")]
    [InlineData("result-field-mismatch")]
    public async Task InvalidDetailFactsFailClosedOnDetailRoute(string corruption)
    {
        var vote = new SeededVote('a', "2026-03-02");
        await using var host = await StartWithVotesAsync(vote);
        var sql = corruption switch
        {
            "fact-text-mismatch" => $"UPDATE vote_revision_facts SET fact_text = 'Fact text differs from citation' WHERE record_id = '{vote.RecordId}' AND revision_number = 1 AND position = 0;",
            "missing-result-fact" => $"DELETE FROM vote_revision_facts WHERE record_id = '{vote.RecordId}' AND revision_number = 1 AND position = 1;",
            "result-field-mismatch" => $"UPDATE vote_revisions SET result = 'Changed result' WHERE record_id = '{vote.RecordId}' AND revision_number = 1;",
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "BEGIN; SET LOCAL session_replication_role = replica; " + sql + " COMMIT;");

        using var response = await host.Client.GetAsync($"/v1/votes/{vote.RecordId}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var members = await host.Client.GetAsync($"/v1/votes/{vote.RecordId}/members");
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
    }

    [Fact]
    public async Task CitationStartingAtLowSurrogateOfSupplementaryCharacterFailsClosed()
    {
        var vote = new SeededVote('a', "2026-03-02");
        await using var host = await StartWithVotesAsync(vote);
        var detailSource = ViewsSeed.ExtractionId(vote.RecordId, 1, "detail");
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            BEGIN;
            SET LOCAL session_replication_role = replica;
            UPDATE vote_revisions SET kind_citation_extraction_id = '{detailSource}',
                kind_citation_start = (SELECT char_length(text) + 1 FROM document_extractions WHERE extraction_id = '{detailSource}'),
                kind_citation_length = 1
             WHERE record_id = '{vote.RecordId}' AND revision_number = 1;
            UPDATE document_extractions SET text = text || chr(119070) WHERE extraction_id = '{detailSource}';
            COMMIT;
            """);

        using var response = await host.Client.GetAsync($"/v1/votes/{vote.RecordId}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var members = await host.Client.GetAsync($"/v1/votes/{vote.RecordId}/members");
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
    }

    [Fact]
    public async Task FourthCitedSourceFailsClosedOnDetailRoute()
    {
        var vote = new SeededVote('a', "2026-03-02");
        await using var host = await StartWithVotesAsync(vote);
        var detailSource = ViewsSeed.ExtractionId(vote.RecordId, 1, "detail");
        var thirdSource = ViewsSeed.ExtractionId(vote.RecordId, 1, "detail-third");
        var fourthSource = ViewsSeed.ExtractionId(vote.RecordId, 1, "detail-fourth");
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            BEGIN;
            SET LOCAL session_replication_role = replica;
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
            SELECT '{thirdSource}', attempt_id, extraction_version, text, text_sha256 FROM document_extractions WHERE extraction_id = '{detailSource}';
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
            SELECT '{fourthSource}', attempt_id, extraction_version, text, text_sha256 FROM document_extractions WHERE extraction_id = '{detailSource}';
            UPDATE vote_revision_facts SET citation_extraction_id = '{thirdSource}' WHERE record_id = '{vote.RecordId}' AND revision_number = 1 AND position = 0;
            UPDATE vote_revisions SET kind_citation_extraction_id = '{fourthSource}' WHERE record_id = '{vote.RecordId}' AND revision_number = 1;
            COMMIT;
            """);

        using var response = await host.Client.GetAsync($"/v1/votes/{vote.RecordId}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task SixtyFifthFactFailsClosedOnDetailRoute()
    {
        var vote = new SeededVote('a', "2026-03-02");
        await using var host = await StartWithVotesAsync(vote);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            BEGIN;
            SET LOCAL session_replication_role = replica;
            INSERT INTO vote_revision_facts (record_id, revision_number, "position", fact_text,
                citation_extraction_id, citation_start, citation_length)
            SELECT '{vote.RecordId}', 1, position, fact_text, citation_extraction_id, citation_start, citation_length
              FROM generate_series(2, 64) AS series(position)
              CROSS JOIN LATERAL (SELECT fact_text, citation_extraction_id, citation_start, citation_length
                                    FROM vote_revision_facts f
                                   WHERE f.record_id = '{vote.RecordId}' AND f.revision_number = 1 AND f.position = 0) source;
            COMMIT;
            """);

        using var response = await host.Client.GetAsync($"/v1/votes/{vote.RecordId}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task SixHundredFirstMemberFailsClosedOnDetailRoute()
    {
        var vote = new SeededVote('a', "2026-03-02");
        await using var host = await StartWithVotesAsync(vote);
        var listId = ViewsSeed.MemberListId(vote.RecordId, 1);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            BEGIN;
            SET LOCAL session_replication_role = replica;
            INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast,
                citation_extraction_id, citation_start, citation_length)
            SELECT '{listId}', position, 'extra-member-' || position, 'X' || lpad(position::text, 6, '0'),
                   vote_cast, citation_extraction_id, citation_start, citation_length
              FROM generate_series(3, 600) AS series(position)
              CROSS JOIN LATERAL (SELECT vote_cast, citation_extraction_id, citation_start, citation_length
                                    FROM vote_member_list_entries e WHERE e.list_id = '{listId}' AND e.position = 0) source;
            COMMIT;
            """);

        using var response = await host.Client.GetAsync($"/v1/votes/{vote.RecordId}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task EmptyFilteredMemberPageReturnsAnEmptyPageWithoutSources()
    {
        var vote = new SeededVote('a', "2026-03-02");
        await using var host = await StartWithVotesAsync(vote);

        using var page = await GetAsync(host, $"/v1/votes/{vote.RecordId}/members?cast=Absent");

        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
        Assert.Empty(page.RootElement.GetProperty("members").EnumerateArray());
    }
}
