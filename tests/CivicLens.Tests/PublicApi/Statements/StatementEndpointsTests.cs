using System.Globalization;
using System.Net;
using System.Text.Json;
using CivicLens.PublicWeb.Publication;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Statements;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class StatementEndpointsTests(PostgresCollection postgres)
{
    private static readonly SeededStatement[] DatedAndUndated =
    [
        new('a', "2026-03-02", ["official-z", "official-a"], ["health"], "Analysis", "Code"),
        new('b', "2026-03-02", ["official-a"], ["health"], "Analysis", "Person"),
        new('c', "2026-03-02", ["official-a"], ["tax"], "Person", "Person"),
        new('d', "2026-03-01", ["official-a"], ["health"], "Person", "Person"),
        new('e', null, ["official-b"], ["health"], "Person", "Person"),
        new('f', null, ["official-b"], ["health"], "Analysis", "Code")
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task CursorWalkReturnsEveryDatedTieAndUndatedRowOnce(int limit)
    {
        await using var host = await StartAsync(DatedAndUndated);
        var seen = new List<char>();
        string? cursor = null;
        var pages = 0;
        do
        {
            using var page = await GetAsync(host, "/v1/statements?limit=" + limit + (cursor is null ? "" : "&cursor=" + cursor));
            var statements = page.RootElement.GetProperty("statements");
            Assert.InRange(statements.GetArrayLength(), 1, limit);
            seen.AddRange(statements.EnumerateArray().Select(item => item.GetProperty("recordId").GetString()![0]));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
            pages++;
        }
        while (cursor is not null && pages < 20);

        Assert.Equal(['a', 'b', 'c', 'd', 'e', 'f'], seen);
        Assert.Equal((6 + limit - 1) / limit, pages);
    }

    [Fact]
    public async Task FiltersCombineAndDateBoundsAreInclusiveWhileUndatedRowsDoNotMatch()
    {
        await using var host = await StartAsync(DatedAndUndated);

        using var filtered = await GetAsync(host, "/v1/statements?official=official-a&issue=health&from=2026-03-01&to=2026-03-02");
        Assert.Equal(['a', 'b', 'd'], Ids(filtered));
        using var unknown = await GetAsync(host, "/v1/statements?official=unknown&issue=unknown");
        Assert.Empty(Ids(unknown));
        using var outside = await GetAsync(host, "/v1/statements?from=2026-03-03");
        Assert.Empty(Ids(outside));
    }

    [Fact]
    public async Task ReturnsThePinnedPublishedRevisionOrderedSpeakersAndStatementSpecificLabels()
    {
        await using var host = await StartAsync(
            DatedAndUndated[0] with { AddUnpublishedRevision = true },
            DatedAndUndated[1], DatedAndUndated[2]);

        using var page = await GetAsync(host, "/v1/statements?from=2026-03-02&to=2026-03-02");
        var rows = page.RootElement.GetProperty("statements").EnumerateArray().ToDictionary(row => row.GetProperty("recordId").GetString()![0]);
        Assert.Equal(1, rows['a'].GetProperty("revisionNumber").GetInt32());
        Assert.Equal("Headline a", rows['a'].GetProperty("headline").GetString());
        Assert.Equal(["official-z", "official-a"], rows['a'].GetProperty("speakerIds").EnumerateArray().Select(id => id.GetString()!).ToArray());
        Assert.Equal(PublishedRecordLabels.StatementModelWording, rows['a'].GetProperty("label").GetString());
        Assert.Equal("model", rows['b'].GetProperty("textOrigin").GetString());
        Assert.Equal(JsonValueKind.Null, rows['b'].GetProperty("label").ValueKind);
        Assert.Equal("person", rows['c'].GetProperty("textOrigin").GetString());
        Assert.Equal(JsonValueKind.Null, rows['c'].GetProperty("label").ValueKind);
    }

    [Fact]
    public async Task ExcludesLegacyRowsWithoutMetadataOrARequiredSpeaker()
    {
        await using var host = await StartAsync(
            new('a', "2026-03-02", ["official-a"], ["health"], "Person", "Person"),
            new('b', "2026-03-01", ["official-a"], ["health"], "Person", "Person", HasMetadata: false),
            new('c', "2026-02-28", [], ["health"], "Person", "Person"));

        using var page = await GetAsync(host, "/v1/statements");
        Assert.Equal(['a'], Ids(page));
    }

    [Fact]
    public async Task CursorIsBoundToActiveReleaseAndRollbackRestoresItsPage()
    {
        await using var host = await StartAsync(DatedAndUndated[..2]);
        using var first = await GetAsync(host, "/v1/statements?limit=1");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        using var changed = await GetAsync(host, "/v1/statements?limit=1&cursor=" + cursor, HttpStatusCode.Conflict);
        Assert.Equal(2, changed.RootElement.GetProperty("release").GetInt32());

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        using var resumed = await GetAsync(host, "/v1/statements?limit=1&cursor=" + cursor);
        Assert.Equal('b', resumed.RootElement.GetProperty("statements")[0].GetProperty("recordId").GetString()![0]);

        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE publication_releases SET is_active = false");
        using var unavailable = await GetAsync(host, "/v1/statements", HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("from=2026-03-02&to=2026-03-01")]
    [InlineData("from=not-a-date")]
    [InlineData("cursor=bad")]
    public async Task InvalidQueryValuesReturnBadRequest(string query)
    {
        await using var host = await StartAsync(DatedAndUndated[..1]);
        using var response = await host.Client.GetAsync("/v1/statements?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<ApiHost> StartAsync(params SeededStatement[] statements)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        try
        {
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
            await StatementsSeed.AddAsync(host.OperationalDatabase, statements);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static async Task<JsonDocument> GetAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    private static char[] Ids(JsonDocument page) =>
        [.. page.RootElement.GetProperty("statements").EnumerateArray().Select(item => item.GetProperty("recordId").GetString()![0])];
}

internal sealed record SeededStatement(char Id, string? Date, string[] Speakers, string[] Issues, string AuthorKind, string Approver,
    bool HasMetadata = true, bool AddUnpublishedRevision = false)
{
    public string RecordId { get; init; } = new(Id, 64);
    public int? LastRelease { get; init; }
}

internal static class StatementsSeed
{
    public static async Task AddAsync(string database, params SeededStatement[] statements)
    {
        foreach (var statement in statements) await ViewsSeed.ExecuteAsync(database, Insert(statement));
    }

    private static string Insert(SeededStatement statement)
    {
        var metadata = statement.HasMetadata ? $"'Headline {statement.Id}', 'Summary {statement.Id}'" : "NULL, NULL";
        var revision = Revision(statement, 1, metadata);
        var unpublished = statement.AddUnpublishedRevision ? Revision(statement, 2, "'Unpublished headline', 'Unpublished summary'") : "";
        var speakers = string.Join('\n', statement.Speakers.Select((speaker, position) => $"INSERT INTO statement_revision_speakers (candidate_id, revision_number, official_id, \"position\") VALUES ('{statement.RecordId}', 1, '{speaker}', {position});"));
        var issues = string.Join('\n', statement.Issues.Select((issue, position) => $"INSERT INTO statement_revision_issues (candidate_id, revision_number, issue_id, \"position\") VALUES ('{statement.RecordId}', 1, '{issue}', {position});"));
        var lastRelease = statement.LastRelease?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
        return $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            VALUES ('statement', '{statement.RecordId}', 1, 1, {lastRelease});
            {revision}
            {unpublished}
            INSERT INTO published_entries (kind, record_id, revision_number, headline, statement_candidate_id, statement_revision_number, approved_by)
            VALUES ('statement', '{statement.RecordId}', 1, 'Entry headline', '{statement.RecordId}', 1, '{statement.Approver}');
            {speakers}
            {issues}
            """;
    }

    private static string Revision(SeededStatement statement, int revision, string metadata)
    {
        var date = statement.Date is null ? "NULL, NULL, NULL, NULL" : $"'{statement.Date}', 'fake-extraction', 0, 1";
        var run = statement.AuthorKind == "Analysis" ? $"'run-{statement.Id}-{revision}'" : "NULL";
        var revisionJson = "{}";
        return $"""
            INSERT INTO statement_revisions (candidate_id, revision_number, author_subject, author_kind, run_id, revision_json,
                created_at_utc_ticks, headline, summary, statement_date, statement_date_evidence_extraction_id,
                statement_date_evidence_start, statement_date_evidence_length)
            VALUES ('{statement.RecordId}', {revision}, 'test', '{statement.AuthorKind}', {run}, '{revisionJson}', 1,
                {metadata}, {date});
            """;
    }
}
