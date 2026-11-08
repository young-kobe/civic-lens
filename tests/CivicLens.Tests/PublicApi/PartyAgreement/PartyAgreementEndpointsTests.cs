using System.Net;
using System.Text.Json;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.PartyAgreement;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PartyAgreementEndpointsTests(PostgresCollection postgres)
{
    [Fact]
    public async Task AgreementCountsOnlyStoredAlignmentsForTheRequestedChamberAndCongress()
    {
        await using var host = await StartAsync(
            new('a', "2026-03-01") { Kind = "Passage", Casts = ["Yea"] },
            new('b', "2026-03-02") { Kind = "Passage", Casts = ["Nay", "Yea", "Yea"] },
            new('c', "2026-03-03") { Kind = "Nomination", Casts = ["Yea", "Yea", "Yea"] },
            new('d', "2026-03-04") { Kind = "Rule" },
            new('0', "2026-03-07") { Kind = "Passage" },
            new('e', "2026-03-05") { Chamber = "Senate", Kind = "Passage" },
            new('f', "2026-03-06") { Congress = 118, Kind = "Passage" });
        await SetAlignmentAsync(host.OperationalDatabase, 'a', "PartiesAgree");
        await SetAlignmentAsync(host.OperationalDatabase, 'b', "Split");
        await SetAlignmentAsync(host.OperationalDatabase, 'c', "Mixed");
        await SetAlignmentAsync(host.OperationalDatabase, '0', null);
        await SetAlignmentAsync(host.OperationalDatabase, 'e', "PartiesAgree");
        await SetAlignmentAsync(host.OperationalDatabase, 'f', "PartiesAgree");

        using var answer = await GetAsync(host, "/v1/party-agreement?chamber=HOUSE&congress=119");
        var agreement = answer.RootElement.GetProperty("agreement");
        Assert.Equal(1, answer.RootElement.GetProperty("release").GetInt32());
        Assert.Equal("house", agreement.GetProperty("chamber").GetString());
        Assert.Equal(119, agreement.GetProperty("congress").GetInt32());
        Assert.Equal(1, agreement.GetProperty("agreed").GetInt32());
        Assert.Equal(1, agreement.GetProperty("split").GetInt32());
        Assert.Equal(1, agreement.GetProperty("mixed").GetInt32());
        Assert.Equal(3, agreement.GetProperty("total").GetInt32());
        Assert.Equal(1, agreement.GetProperty("nominations").GetInt32());
        Assert.Equal(new[] { "passage", "nomination" }, agreement.GetProperty("kinds").EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString()).ToArray());
        Assert.Equal(2, agreement.GetProperty("kinds")[0].GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AgreementReadsTheExactActiveVoteRevision()
    {
        await using var host = await StartAsync(new SeededVote('a', "2026-03-01") { ActiveRevision = 2 });
        await SetAlignmentAsync(host.OperationalDatabase, 'a', "Split", revision: 1);
        await SetAlignmentAsync(host.OperationalDatabase, 'a', "PartiesAgree", revision: 2);

        using var answer = await GetAsync(host, "/v1/party-agreement?chamber=house&congress=119");

        Assert.Equal(1, answer.RootElement.GetProperty("agreement").GetProperty("agreed").GetInt32());
        Assert.Equal(0, answer.RootElement.GetProperty("agreement").GetProperty("split").GetInt32());
    }

    [Theory]
    [InlineData("House", "Rule")]
    [InlineData("Senate", "Passage")]
    public async Task ValidEmptyGroupReturnsZeroAndUnalignedKindsAreOmitted(string chamber, string kind)
    {
        await using var host = await StartAsync(new SeededVote('a', "2026-03-01") { Chamber = chamber, Kind = kind });
        if (chamber == "Senate") await SetAlignmentAsync(host.OperationalDatabase, 'a', "PartiesAgree");

        using var answer = await GetAsync(host, "/v1/party-agreement?chamber=house&congress=119");
        var agreement = answer.RootElement.GetProperty("agreement");
        Assert.Empty(agreement.GetProperty("kinds").EnumerateArray());
        Assert.Equal(0, agreement.GetProperty("total").GetInt32());
        Assert.Equal(0, agreement.GetProperty("nominations").GetInt32());
    }

    [Fact]
    public async Task MissingTypedActiveRevisionHidesAgreementUntilThatVoteLeavesTheRelease()
    {
        await using var host = await StartAsync(new SeededVote('a', "2026-03-01"));
        await SetAlignmentAsync(host.OperationalDatabase, 'a', "Split");
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number)
            VALUES ('vote', '{new string('f', 64)}', 1, 1);
            """);

        using (var answer = await GetAsync(host, "/v1/party-agreement?chamber=house&congress=119"))
            Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("agreement").ValueKind);

        await ViewsSeed.ExecuteAsync(host.OperationalDatabase,
            $"UPDATE publication_record_states SET last_release = 1 WHERE record_id = '{new string('f', 64)}'");
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero));
        using var current = await GetAsync(host, "/v1/party-agreement?chamber=house&congress=119");
        Assert.Equal(1, current.RootElement.GetProperty("agreement").GetProperty("split").GetInt32());
    }

    [Fact]
    public async Task PublicationWithdrawalAndRollbackSelectOnlyTheActiveReleaseVotes()
    {
        await using var host = await StartAsync(
            new('a', "2026-03-01"), new('b', "2026-03-02") { FirstRelease = 2 }, new('c', "2026-03-03") { LastRelease = 1 });
        await SetAlignmentAsync(host.OperationalDatabase, 'a', "PartiesAgree");
        await SetAlignmentAsync(host.OperationalDatabase, 'b', "Split");
        await SetAlignmentAsync(host.OperationalDatabase, 'c', "Mixed");

        foreach (var release in new[] { 1, 2, 1 })
        {
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, release,
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(release));
            using var answer = await GetAsync(host, "/v1/party-agreement?chamber=house&congress=119");
            var agreement = answer.RootElement.GetProperty("agreement");
            Assert.Equal(release, answer.RootElement.GetProperty("release").GetInt32());
            Assert.Equal(1, agreement.GetProperty("agreed").GetInt32());
            Assert.Equal(release == 2 ? 1 : 0, agreement.GetProperty("split").GetInt32());
            Assert.Equal(release == 1 ? 1 : 0, agreement.GetProperty("mixed").GetInt32());
            Assert.Equal(2, agreement.GetProperty("total").GetInt32());
        }
    }

    [Theory]
    [InlineData("/v1/party-agreement?congress=119")]
    [InlineData("/v1/party-agreement?chamber=house")]
    [InlineData("/v1/party-agreement?chamber=other&congress=119")]
    [InlineData("/v1/party-agreement?chamber=house&congress=0")]
    [InlineData("/v1/party-agreement?chamber=house&congress=119&congress=118")]
    [InlineData("/v1/party-agreement?chamber=house&chamber=senate&congress=119")]
    public async Task RequiredAndInvalidFiltersAnswerBadRequest(string path)
    {
        await using var host = await StartAsync();
        using var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NoActiveReleaseAnswersServiceUnavailable()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        using var response = await host.Client.GetAsync("/v1/party-agreement?chamber=house&congress=119");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private async Task<ApiHost> StartAsync(params SeededVote[] votes)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, votes);
        return host;
    }

    private static Task SetAlignmentAsync(string database, char id, string? alignment, int revision = 1) =>
        ViewsSeed.ExecuteAsync(database, $"UPDATE vote_revisions SET columns_written = true, alignment = {(alignment is null ? "NULL" : $"'{alignment}'")} WHERE record_id = '{new string(id, 64)}' AND revision_number = {revision}");

    private static async Task<JsonDocument> GetAsync(ApiHost host, string path)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(body);
    }
}
