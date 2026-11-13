using System.Net;
using System.Text.Json;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Records;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class CorrectionHistoryEndpointsTests(PostgresCollection postgres)
{
    private static readonly string Id = new('a', 64);

    [Fact]
    public async Task RouteUsesRecordKindValidationAndDistinguishesMissingReleaseFromUnpublishedRecords()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        foreach (var kind in Enum.GetValues<PublishedRecordKind>())
        {
            var recordId = kind == PublishedRecordKind.DocumentChange ? new string('b', 32) : Id;
            using var noRelease = await host.Client.GetAsync($"/v1/records/{PublicationProtocol.KindText(kind)}/{recordId}/corrections");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, noRelease.StatusCode);
        }

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, DateTimeOffset.UnixEpoch);
        foreach (var kind in Enum.GetValues<PublishedRecordKind>())
        {
            var recordId = kind == PublishedRecordKind.DocumentChange ? new string('b', 32) : Id;
            using var missing = await host.Client.GetAsync($"/v1/records/{PublicationProtocol.KindText(kind)}/{recordId}/corrections");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using var unknownKind = await host.Client.GetAsync($"/v1/records/unknown/{Id}/corrections");
        using var malformed = await host.Client.GetAsync("/v1/records/statement/no/corrections");
        using var malformedWidth = await host.Client.GetAsync($"/v1/records/statement/{new string('b', 32)}/corrections");
        Assert.Equal(HttpStatusCode.NotFound, unknownKind.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformedWidth.StatusCode);

        foreach (var kind in Enum.GetValues<PublishedRecordKind>())
        {
            var recordId = kind == PublishedRecordKind.DocumentChange ? new string('d', 32) : new string('e', 64);
            await SeedEmptyBindingAsync(host.OperationalDatabase, 1, kind, recordId);
            using var emptyBinding = await host.Client.GetAsync($"/v1/records/{PublicationProtocol.KindText(kind)}/{recordId}/corrections");
            Assert.Equal(HttpStatusCode.OK, emptyBinding.StatusCode);
            using var body = JsonDocument.Parse(await emptyBinding.Content.ReadAsStringAsync());
            Assert.Empty(body.RootElement.GetProperty("corrections").EnumerateArray());
        }

        await SeedRecordWithoutHistoryAsync(host.OperationalDatabase, new string('c', 64));
        using var missingBinding = await host.Client.GetAsync($"/v1/records/action/{new string('c', 64)}/corrections");
        Assert.Equal(HttpStatusCode.InternalServerError, missingBinding.StatusCode);
    }

    [Fact]
    public async Task DetailKeepsBoundHistoryOrderAndTimestampOffsetAndFailsClosedOnDigestMismatch()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, DateTimeOffset.UnixEpoch);
        var corrections = MaximumHistory();
        var historyId = PublishedCorrectionHistoryIdentity.Of(corrections);
        await SeedAsync(host.OperationalDatabase, 2, 63, historyId, corrections);
        await SeedEmptyAsync(host.OperationalDatabase, 2, new string('b', 64));
        await SeedEarlierHistoryAsync(host.OperationalDatabase, new string('a', 64));

        using var empty = await host.Client.GetAsync($"/v1/records/action/{new string('b', 64)}/corrections");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using (var emptyBody = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()))
            Assert.Empty(emptyBody.RootElement.GetProperty("corrections").EnumerateArray());

        using var response = await host.Client.GetAsync($"/v1/records/action/{Id}/corrections");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(2, root.GetProperty("release").GetInt32());
        Assert.Equal("action", root.GetProperty("kind").GetString());
        Assert.Equal(Id, root.GetProperty("recordId").GetString());
        Assert.Equal(63, root.GetProperty("revisionNumber").GetInt32());
        var entries = root.GetProperty("corrections").EnumerateArray().ToArray();
        Assert.Equal(PublishedCorrection.MaximumEntries, entries.Length);
        Assert.Equal(PublishedCorrection.MaximumNotedEntries, entries.Count(entry => entry.GetProperty("note").ValueKind != JsonValueKind.Null));
        Assert.Equal(62, entries[0].GetProperty("replacedRevisionNumber").GetInt32());
        Assert.Equal("2022-06-05T04:03:02-04:00", entries[0].GetProperty("publishedAtUtc").GetString());
        Assert.Equal(JsonValueKind.Null, entries[0].GetProperty("note").ValueKind);
        Assert.True(entries[0].GetProperty("factsUnchanged").GetBoolean());
        Assert.Equal(61, entries[1].GetProperty("replacedRevisionNumber").GetInt32());
        Assert.Equal("2021-04-03T01:02:03.0000007+05:00", entries[2].GetProperty("publishedAtUtc").GetString());
        Assert.Equal("Corrected the date.", entries[2].GetProperty("note").GetString());

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, DateTimeOffset.UnixEpoch);
        using var earlier = await host.Client.GetAsync($"/v1/records/action/{Id}/corrections");
        Assert.Equal(HttpStatusCode.OK, earlier.StatusCode);
        using var earlierBody = JsonDocument.Parse(await earlier.Content.ReadAsStringAsync());
        Assert.Equal(1, earlierBody.RootElement.GetProperty("release").GetInt32());
        Assert.Empty(earlierBody.RootElement.GetProperty("corrections").EnumerateArray());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, DateTimeOffset.UnixEpoch);

        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE published_correction_history_entries SET note = 'Changed after publication' WHERE history_id = '{historyId}' AND \"position\" = 2");
        using var corrupt = await host.Client.GetAsync($"/v1/records/action/{Id}/corrections");
        Assert.Equal(HttpStatusCode.InternalServerError, corrupt.StatusCode);

        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE published_correction_history_entries SET note = 'Corrected the date.' WHERE history_id = '{historyId}' AND \"position\" = 2; UPDATE published_correction_histories SET correction_count = 61 WHERE history_id = '{historyId}'");
        using var wrongCount = await host.Client.GetAsync($"/v1/records/action/{Id}/corrections");
        Assert.Equal(HttpStatusCode.InternalServerError, wrongCount.StatusCode);

        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE published_correction_histories SET correction_count = 62 WHERE history_id = '{historyId}'; DELETE FROM published_correction_history_entries WHERE history_id = '{historyId}' AND \"position\" = 0");
        using var wrongPosition = await host.Client.GetAsync($"/v1/records/action/{Id}/corrections");
        Assert.Equal(HttpStatusCode.InternalServerError, wrongPosition.StatusCode);
    }

    private static Task SeedAsync(string database, int release, int revision, string historyId, PublishedCorrection[] corrections)
    {
        var rows = string.Join(",\n", corrections.Select((entry, position) =>
            $"('{historyId}', {position}, {entry.ReplacedRevisionNumber}, {entry.PublishedAtUtc.UtcTicks}, {(int)entry.PublishedAtUtc.Offset.TotalMinutes}, {(entry.Note is null ? "NULL" : $"'{entry.Note}'")}, {entry.FactsUnchanged.ToString().ToLowerInvariant()})"));
        return ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            VALUES ('action', '{Id}', 1, {revision}, NULL);
            INSERT INTO published_entries (kind, record_id, revision_number, headline, action_record_id, action_revision_number, bill_title)
            VALUES ('action', '{Id}', {revision}, 'Headline', '{Id}', {revision}, 'Bill');
            INSERT INTO published_correction_histories (history_id, encoding_version, correction_count)
            VALUES ('{historyId}', {PublishedCorrectionHistoryIdentity.CurrentVersion}, {corrections.Length});
            INSERT INTO published_correction_history_entries (history_id, "position", replaced_revision_number, published_at_utc_ticks, published_offset_minutes, note, facts_unchanged)
            VALUES {rows};
            INSERT INTO release_correction_histories (release_number, kind, record_id, revision_number, history_id)
            VALUES ({release}, 'action', '{Id}', {revision}, '{historyId}');
            """);
    }

    private static PublishedCorrection[] MaximumHistory()
    {
        var corrections = new List<PublishedCorrection>(PublishedCorrection.MaximumEntries);
        var revision = PublishedCorrection.MaximumEntries;
        for (var gap = 0; gap <= PublishedCorrection.MaximumNotedEntries; gap++)
        {
            corrections.Add(new PublishedCorrection
            {
                ReplacedRevisionNumber = revision--,
                PublishedAtUtc = gap == 0 ? new DateTimeOffset(2022, 6, 5, 4, 3, 2, TimeSpan.FromHours(-4)) : DateTimeOffset.UnixEpoch,
                FactsUnchanged = true
            });
            corrections.Add(new PublishedCorrection
            {
                ReplacedRevisionNumber = revision--,
                PublishedAtUtc = DateTimeOffset.UnixEpoch
            });
            if (gap < PublishedCorrection.MaximumNotedEntries)
                corrections.Add(new PublishedCorrection
                {
                    ReplacedRevisionNumber = revision--,
                    PublishedAtUtc = gap == 0 ? new DateTimeOffset(2021, 4, 3, 1, 2, 3, TimeSpan.FromHours(5)).AddTicks(7) : DateTimeOffset.UnixEpoch,
                    Note = "Corrected the date."
                });
        }
        return [.. corrections];
    }

    private static Task SeedEmptyAsync(string database, int release, string recordId)
    {
        var historyId = PublishedCorrectionHistoryIdentity.Of([]);
        return ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            VALUES ('action', '{recordId}', 1, 1, NULL);
            INSERT INTO published_entries (kind, record_id, revision_number, headline, action_record_id, action_revision_number, bill_title)
            VALUES ('action', '{recordId}', 1, 'Headline', '{recordId}', 1, 'Bill');
            INSERT INTO published_correction_histories (history_id, encoding_version, correction_count)
            VALUES ('{historyId}', {PublishedCorrectionHistoryIdentity.CurrentVersion}, 0);
            INSERT INTO release_correction_histories (release_number, kind, record_id, revision_number, history_id)
            VALUES ({release}, 'action', '{recordId}', 1, '{historyId}');
            """);
    }

    private static Task SeedEmptyBindingAsync(string database, int release, PublishedRecordKind kind, string recordId)
    {
        var kindText = PublicationProtocol.KindText(kind);
        var historyId = PublishedCorrectionHistoryIdentity.Of([]);
        return ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            INSERT INTO published_correction_histories (history_id, encoding_version, correction_count)
            VALUES ('{historyId}', {PublishedCorrectionHistoryIdentity.CurrentVersion}, 0)
            ON CONFLICT (history_id) DO NOTHING;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            VALUES ('{kindText}', '{recordId}', {release}, 1, NULL);
            INSERT INTO release_correction_histories (release_number, kind, record_id, revision_number, history_id)
            VALUES ({release}, '{kindText}', '{recordId}', 1, '{historyId}');
            """);
    }

    private static Task SeedRecordWithoutHistoryAsync(string database, string recordId) => ViewsSeed.ExecuteAsync(database, $"""
        SET session_replication_role = replica;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
        VALUES ('action', '{recordId}', 1, 1, NULL);
        INSERT INTO published_entries (kind, record_id, revision_number, headline, action_record_id, action_revision_number, bill_title)
        VALUES ('action', '{recordId}', 1, 'Headline', '{recordId}', 1, 'Bill');
        """);

    private static Task SeedEarlierHistoryAsync(string database, string recordId)
    {
        var historyId = PublishedCorrectionHistoryIdentity.Of([]);
        return ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            INSERT INTO release_correction_histories (release_number, kind, record_id, revision_number, history_id)
            VALUES (1, 'action', '{recordId}', 63, '{historyId}');
            """);
    }
}
