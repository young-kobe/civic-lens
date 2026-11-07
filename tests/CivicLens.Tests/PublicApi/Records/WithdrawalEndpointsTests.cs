using System.Net;
using System.Text.Json;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Records;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class WithdrawalEndpointsTests(PostgresCollection postgres)
{
    private static readonly string Id = new('a', 64);

    [Fact]
    public async Task NoticeUsesOnlyTheActiveReleaseEpisodeAndKeepsEveryTypedCorrection()
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await using (host)
        {
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 6, new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero));
            var first = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero).UtcTicks;
            var withdrawn = new DateTimeOffset(2026, 6, 2, 3, 4, 5, TimeSpan.Zero).UtcTicks;
            var edit = new DateTimeOffset(2026, 6, 3, 3, 4, 5, TimeSpan.Zero).UtcTicks;
            var correctionRows = string.Join(",\n", Enumerable.Range(0, 62).Select(position =>
            {
                var note = position % 3 == 2 ? $"'Correction {position}'" : "NULL";
                var unchanged = position % 3 == 1 ? "true" : "false";
                return $"('statement', '{Id}', 6, {position}, {100 - position}, {edit}, {note}, {unchanged})";
            }));
            await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
                SET session_replication_role = replica;
                INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES
                    ('statement', '{Id}', {first}), ('action', '{Id}', {first});
                INSERT INTO publication_withdrawals (kind, record_id, withdrawn_revision_number, withdrawn_at_utc_ticks, note, first_release, last_release) VALUES
                    ('statement', '{Id}', 3, {withdrawn}, 'Old notice', 2, 3),
                    ('statement', '{Id}', 101, {withdrawn}, 'Current notice', 6, NULL),
                    ('action', '{Id}', 2, {withdrawn}, 'Other kind', 6, NULL);
                INSERT INTO publication_withdrawal_corrections (kind, record_id, first_release, "position", replaced_revision_number, published_at_utc_ticks, note, facts_unchanged) VALUES
                    ('statement', '{Id}', 2, 0, 2, {edit}, 'Old correction', false),
                    {correctionRows};
                """);

            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero));
            using var returned = await host.Client.GetAsync($"/v1/records/statement/{Id}/withdrawal");
            Assert.Equal(HttpStatusCode.NotFound, returned.StatusCode);
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 5, new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero));
            using var returnedAgain = await host.Client.GetAsync($"/v1/records/statement/{Id}/withdrawal");
            Assert.Equal(HttpStatusCode.NotFound, returnedAgain.StatusCode);
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 6, new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero));
            using var response = await host.Client.GetAsync($"/v1/records/statement/{Id}/withdrawal");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.Equal(6, root.GetProperty("release").GetInt32());
            var notice = root.GetProperty("withdrawal");
            Assert.Equal("statement", notice.GetProperty("kind").GetString());
            Assert.Equal("Current notice", notice.GetProperty("note").GetString());
            Assert.Equal(101, notice.GetProperty("withdrawnRevisionNumber").GetInt32());
            Assert.Equal("2020-01-02T00:00:00+00:00", notice.GetProperty("firstPublishedAtUtc").GetString());
            Assert.Equal("2026-06-02T03:04:05+00:00", notice.GetProperty("withdrawnAtUtc").GetString());
            var corrections = notice.GetProperty("corrections").EnumerateArray().ToArray();
            Assert.Equal(62, corrections.Length);
            Assert.Equal(20, corrections.Count(item => item.GetProperty("note").ValueKind != JsonValueKind.Null));
            Assert.Equal(Enumerable.Range(0, 62).Select(position => 100 - position),
                corrections.Select(item => item.GetProperty("replacedRevisionNumber").GetInt32()));
            Assert.Equal("Correction 2", corrections[2].GetProperty("note").GetString());
            Assert.True(corrections[1].GetProperty("factsUnchanged").GetBoolean());
            Assert.Equal("2026-06-03T03:04:05+00:00", corrections[0].GetProperty("publishedAtUtc").GetString());

            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero));
            using var priorEpisode = await host.Client.GetAsync($"/v1/records/statement/{Id}/withdrawal");
            using var priorBody = JsonDocument.Parse(await priorEpisode.Content.ReadAsStringAsync());
            Assert.Equal("Old notice", priorBody.RootElement.GetProperty("withdrawal").GetProperty("note").GetString());

            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            using var beforeWithdrawal = await host.Client.GetAsync($"/v1/records/statement/{Id}/withdrawal");
            Assert.Equal(HttpStatusCode.NotFound, beforeWithdrawal.StatusCode);
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 6, new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero));

            using var otherKind = await host.Client.GetAsync($"/v1/records/action/{Id}/withdrawal");
            using var otherBody = JsonDocument.Parse(await otherKind.Content.ReadAsStringAsync());
            var otherNotice = otherBody.RootElement.GetProperty("withdrawal");
            Assert.Equal("Other kind", otherNotice.GetProperty("note").GetString());
            Assert.Empty(otherNotice.GetProperty("corrections").EnumerateArray());

            using var invalid = await host.Client.GetAsync($"/v1/records/not-a-kind/{Id}/withdrawal");
            Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
            using var malformed = await host.Client.GetAsync("/v1/records/statement/no/withdrawal");
            Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
            using var absent = await host.Client.GetAsync($"/v1/records/vote/{Id}/withdrawal");
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE publication_releases SET is_active = false;");
            using var noActiveRelease = await host.Client.GetAsync($"/v1/records/statement/{Id}/withdrawal");
            Assert.Equal(HttpStatusCode.NotFound, noActiveRelease.StatusCode);
        }
    }
}
