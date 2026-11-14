using System.Reflection;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReleaseIssuePartyCountsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Published = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ActivateReleaseAsync(connectionString, 1, Published);
        await ViewsSeed.ExecuteAsync(connectionString, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health', 'Definition'), (1, 'tax', 'Tax', 'Definition')");
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task AnIndependentIsCountedUnderTheOwnPartyLabelAndNotTheCaucusBecauseTheOwnerChoseTheLabelOfTheTerm()
    {
        await AddOfficialAsync(1, "p1", Term("Independent", "Democratic", "2025-01-03", null));
        await AddOfficialAsync(1, "p2", Term("Democratic", "Democratic", "2025-01-03", null));
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1", "2025-02-01"), Bill('2', "p2", "2025-02-01"), Bill('3', "p2", "2025-02-01"));

        await FillAsync(1, 119);

        Assert.Equal(["health|Democratic|2", "health|Independent|1"], await PartiesAsync(1));
        Assert.Equal(["health|0", "tax|0"], await UnknownAsync(1));
    }

    [Fact]
    public async Task ASponsorWhoChangedPartyIsCountedByTheTermThatHeldOnTheSponsorshipDateIncludingTheLastDayOfATerm()
    {
        await AddOfficialAsync(1, "p1",
            Term("Republican", "Republican", "2025-01-03", "2025-06-30"), Term("Independent", "Independent", "2025-07-01", null));
        await BillsSeed.AddBillsAsync(connectionString,
            Bill('1', "p1", "2025-01-03"), Bill('2', "p1", "2025-06-30"), Bill('3', "p1", "2025-07-01"), Bill('4', "p1", "2026-02-01"));

        await FillAsync(1, 119);

        Assert.Equal(["health|Independent|2", "health|Republican|2"], await PartiesAsync(1));
    }

    [Fact]
    public async Task AMemberWhoChangedPartyBetweenTermsIsCountedByTheTermPartyOnTheSponsorshipDateAsTheBillPageShowsIt()
    {
        await AddOfficialAsync(1, "p1",
            Term("Republican", "Republican", "2025-01-03", "2025-12-31"), Term("Democratic", "Democratic", "2026-01-01", null));
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1", "2025-03-01"), Bill('2', "p1", "2025-12-31"), Bill('3', "p1", "2026-03-01"));

        await FillAsync(1, 119);

        Assert.Equal(["health|Democratic|1", "health|Republican|2"], await PartiesAsync(1));
        Assert.Equal(await PartiesAsync(1), await ReadAsync(
            "SELECT 'health|' || party || '|' || count(*) FROM public_api.bill_sponsorship_parties GROUP BY party ORDER BY party"));
    }

    [Fact]
    public async Task ABillWhoseSponsorPartyNoTermFixesIsCountedAsUnknownBecauseThePartsMustAddUpToTheBase()
    {
        await AddOfficialAsync(1, "p1", Term("Republican", "Republican", "2025-01-03", null));
        await AddOfficialAsync(1, "late", Term("Democratic", "Democratic", "2025-09-01", null));
        await AddOfficialAsync(1, "blank", Term(null, null, "2025-01-03", null));
        await AddOfficialAsync(1, "p2", Term("Democratic", "Democratic", "2025-01-03", null));
        await BillsSeed.AddBillsAsync(connectionString,
            Bill('1', "p1", "2025-02-01"),
            Bill('2', "stranger", "2025-02-01"),
            Bill('3', "late", "2025-02-01"),
            Bill('4', "blank", "2025-02-01"),
            new SeededBill('5', "2025-02-01") { BillNumber = 5, Issues = ["health"], Sponsorships = [new("p2", "Cosponsor", "2025-02-01")] },
            new SeededBill('6', "2025-02-01") { BillNumber = 6, Issues = ["health"], Sponsorships = [new("p1", "Sponsor", "2025-02-01"), new("p2", "Sponsor", "2025-02-01")] },
            new SeededBill('7', "2025-02-01") { BillNumber = 7, Issues = ["health"], Sponsorships = [new("p1", "Sponsor", "2025-02-01"), new("p1", "Sponsor", "2025-02-01")] },
            new SeededBill('8', "2025-02-01") { BillNumber = 8, Issues = ["health"], Sponsorships = [new("p1", "Sponsor", "2025-02-01"), new("stranger", "Sponsor", "2025-02-01")] });

        await FillAsync(1, 119);

        Assert.Equal(["health|Republican|2"], await PartiesAsync(1));
        Assert.Equal(["health|6", "tax|0"], await UnknownAsync(1));
    }

    [Fact]
    public async Task TwoOverlappingTermsOfDifferentPartiesOnTheSponsorshipDateCountAsUnknownBecauseNoSinglePartyHeldTheSeat()
    {
        await AddOfficialAsync(1, "p1",
            Term("Republican", "Republican", "2025-01-03", null), Term("Democratic", "Democratic", "2025-01-03", "2025-12-31"));
        await AddOfficialAsync(1, "same",
            Term("Republican", "Republican", "2025-01-03", null), Term("Republican", "Republican", "2025-01-03", "2025-12-31"));
        await BillsSeed.AddBillsAsync(connectionString,
            Bill('1', "p1", "2025-02-01"), Bill('2', "p1", "2026-02-01"), Bill('3', "same", "2025-02-01"));

        await FillAsync(1, 119);

        Assert.Equal(["health|Republican|2"], await PartiesAsync(1));
        Assert.Equal(["health|1", "tax|0"], await UnknownAsync(1));
    }

    [Fact]
    public async Task ABlankTermPartyCountsAsUnknownAndDoesNotFailThePublishBecauseTheTableRefusesABlankLabel()
    {
        await AddOfficialAsync(1, "empty", Term("", "", "2025-01-03", null));
        await AddOfficialAsync(1, "spaces", Term("  ", "  ", "2025-01-03", null));
        await AddOfficialAsync(1, "p1", Term("Republican", "Republican", "2025-01-03", null));
        await BillsSeed.AddBillsAsync(connectionString,
            Bill('1', "empty", "2025-02-01"), Bill('2', "spaces", "2025-02-01"), Bill('3', "p1", "2025-02-01"));

        await FillAsync(1, 119);

        Assert.Equal(["health|Republican|1"], await PartiesAsync(1));
        Assert.Equal(["health|2", "tax|0"], await UnknownAsync(1));
    }

    [Fact]
    public async Task ASponsorRowIsCountedWhateverItsWithdrawnDateSaysBecauseTheEligibilityOfTheBillDoesNotLookAtTheDate()
    {
        await AddOfficialAsync(1, "p1", Term("Republican", "Republican", "2025-01-03", null));
        await BillsSeed.AddBillsAsync(connectionString,
            Bill('1', "p1", "2025-02-01") with { Sponsorships = [new("p1", "Sponsor", "2025-02-01", "2025-03-01")] });

        await FillIssueCountsAsync(1, 119);
        await FillAsync(1, 119);

        Assert.Equal(["health|1", "tax|0"], await BaseAsync(1));
        Assert.Equal(["health|Republican|1"], await PartiesAsync(1));
        Assert.Equal(["health|0", "tax|0"], await UnknownAsync(1));
    }

    [Fact]
    public async Task ThePartsAndTheUnknownCountAddUpToTheHomeTileBillCountOfEachIssueBecauseOneRuleSelectsAndCountsTheBills()
    {
        await ViewsSeed.ActivateReleaseAsync(connectionString, 2, Published.AddDays(1));
        await ViewsSeed.ExecuteAsync(connectionString, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (2, 'health', 'Health', 'Definition'), (2, 'tax', 'Tax', 'Definition')");
        await AddOfficialAsync(1, "p1", Term("Republican", "Republican", "2025-01-03", null));
        await AddOfficialAsync(2, "p1", Term("Republican", "Republican", "2025-01-03", null));
        await BillsSeed.AddBillsAsync(connectionString,
            Bill('1', "p1", "2025-02-01"),
            Bill('2', "p1", "2025-02-01") with { Issues = ["health", "tax"] },
            Bill('3', "ghost", "2025-02-01"),
            Bill('4', "p1", "2025-02-01") with { Issues = [], UntrackedIssues = ["health"] },
            Bill('5', "p1", "2025-02-01") with { Congress = 118 },
            Bill('6', "p1", "2025-02-01") with { FirstRelease = 2 },
            Bill('7', "p1", "2025-02-01") with { LastRelease = 1 },
            new SeededBill('8', "2025-02-01") { BillNumber = 8, Issues = ["tax"], Sponsorships = [] });

        await FillIssueCountsAsync(1, 119);
        await FillAsync(1, 119);
        await FillIssueCountsAsync(2, 119);
        await FillAsync(2, 119);

        Assert.Equal(["health|4", "tax|1"], await BaseAsync(1));
        Assert.Equal(["health|Republican|3", "tax|Republican|1"], await PartiesAsync(1));
        Assert.Equal(["health|1", "tax|0"], await UnknownAsync(1));
        Assert.Equal(["health|4", "tax|1"], await BaseAsync(2));
        Assert.Equal(["health|Republican|3", "tax|Republican|1"], await PartiesAsync(2));
        Assert.Equal(["health|1", "tax|0"], await UnknownAsync(2));
        Assert.Empty(await ReadAsync("""
            SELECT i.release_number || '|' || i.issue_id FROM release_issues i JOIN release_issue_party_lineups l USING (release_number, issue_id)
             WHERE i.bills <> l.unknown_bills + coalesce((SELECT sum(c.bills) FROM release_issue_party_counts c
                                                           WHERE c.release_number = l.release_number AND c.issue_id = l.issue_id), 0)
            """));
    }

    [Fact]
    public async Task OnlyBillsOfTheReleaseDayCongressAreCountedBecauseTheBaseIsTheHomeTileOfThatCongress()
    {
        await AddOfficialAsync(1, "p1", Term("Republican", "Republican", "2019-01-03", null));
        await BillsSeed.AddBillsAsync(connectionString,
            Bill('1', "p1", "2019-02-01") with { Congress = 116 },
            Bill('2', "p1", "2025-02-01"),
            Bill('3', "p1", "2025-02-01"));

        await FillAsync(1, 119);

        Assert.Equal(["health|Republican|2"], await PartiesAsync(1));
        Assert.Equal(["health|119|2"], await BaseWithCongressAsync(1));
    }

    [Fact]
    public async Task TheTermsOfTheReleaseThatIsBeingPublishedDecideThePartyBecauseEachReleaseHoldsItsOwnNames()
    {
        await ViewsSeed.ActivateReleaseAsync(connectionString, 2, Published.AddDays(1));
        await ViewsSeed.ExecuteAsync(connectionString, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (2, 'health', 'Health', 'Definition')");
        await AddOfficialAsync(1, "p1", Term("Republican", "Republican", "2025-01-03", null));
        await AddOfficialAsync(2, "p1", Term("Democratic", "Democratic", "2025-01-03", null));
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1", "2025-02-01"));

        await FillAsync(1, 119);
        await FillAsync(2, 119);

        Assert.Equal(["health|Republican|1"], await PartiesAsync(1));
        Assert.Equal(["health|Democratic|1"], await PartiesAsync(2));
    }

    [Fact]
    public async Task AReleaseWithNoCongressGetsNoLineUpRowsBecauseItsCountsAreAbsentToo()
    {
        await AddOfficialAsync(1, "p1", Term("Republican", "Republican", "2025-01-03", null));
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1", "2025-02-01"));

        await FillAsync(1, null);

        Assert.Empty(await UnknownAsync(1));
    }

    [Fact]
    public async Task AnExactReplayIsAcceptedAndAChangedOrIncompleteSnapshotIsRefusedAndKept()
    {
        await AddOfficialAsync(1, "p1", Term("Republican", "Republican", "2025-01-03", null));
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1", "2025-02-01"), Bill('2', "ghost", "2025-02-01"));
        await FillAsync(1, 119);
        await FillAsync(1, 119);
        Assert.Equal(["health|Republican|1"], await PartiesAsync(1));

        await ViewsSeed.ExecuteAsync(connectionString, "UPDATE release_issue_party_lineups SET unknown_bills = 0 WHERE release_number = 1 AND issue_id = 'health'");
        await Assert.ThrowsAsync<BillCountRefusedException>(() => FillAsync(1, 119));
        Assert.Equal(["health|0", "tax|0"], await UnknownAsync(1));

        await ViewsSeed.ExecuteAsync(connectionString, """
            UPDATE release_issue_party_lineups SET unknown_bills = 1 WHERE release_number = 1 AND issue_id = 'health';
            DELETE FROM release_issue_party_counts WHERE release_number = 1;
            """);
        await Assert.ThrowsAsync<BillCountRefusedException>(() => FillAsync(1, 119));
        Assert.Empty(await PartiesAsync(1));
    }

    private static string Term(string? party, string? caucus, string start, string? end) =>
        $"""({(party is null ? "NULL" : $"'{party}'")}, {(caucus is null ? "NULL" : $"'{caucus}'")}, '{start}', {(end is null ? "NULL" : $"'{end}'")})""";

    private static SeededBill Bill(char id, string sponsor, string sponsoredOn) =>
        new(id, sponsoredOn) { BillNumber = id, Issues = ["health"], Sponsorships = [new(sponsor, "Sponsor", sponsoredOn)] };

    private async Task AddOfficialAsync(int release, string id, params string[] terms) => await ViewsSeed.ExecuteAsync(connectionString, $"""
        INSERT INTO release_officials (release_number, official_id, name) VALUES ({release}, '{id}', '{id}');
        INSERT INTO release_official_terms (release_number, official_id, "position", office, kind, state, district, party, caucus, start_on, end_on)
        VALUES {string.Join(", ", terms.Select((term, position) => $"({release}, '{id}', {position}, 'Representative', 'Representative', 'TX', 1, {term[1..]}"))};
        """);

    private async Task FillIssueCountsAsync(int release, int congress)
    {
        await using var db = NewContext();
        var type = typeof(PostgresPublicationStore).Assembly.GetType("CivicLens.Infrastructure.Publication.ReleaseIssueCounts", throwOnError: true)!;
        var task = (Task<int>)type.GetMethod("FillAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [db, release, (int?)congress, CancellationToken.None])!;
        await task;
    }

    private async Task FillAsync(int release, int? congress)
    {
        if (congress is { } selected && !await IsFilledAsync(release)) await FillIssueCountsAsync(release, selected);
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var type = typeof(PostgresPublicationStore).Assembly.GetType("CivicLens.Infrastructure.Publication.ReleaseIssuePartyCounts", throwOnError: true)!;
        await (Task)type.GetMethod("FillAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [db, release, congress, CancellationToken.None])!;
        await transaction.CommitAsync();
    }

    private async Task<bool> IsFilledAsync(int release) =>
        (await ReadAsync($"SELECT count(*) FILTER (WHERE count_congress IS NOT NULL)::text FROM release_issues WHERE release_number = {release}"))[0] != "0";

    private CollectionAttemptDbContext NewContext() => new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private Task<string[]> PartiesAsync(int release) => ReadAsync($"SELECT issue_id || '|' || party || '|' || bills FROM release_issue_party_counts WHERE release_number = {release} ORDER BY issue_id, party");

    private Task<string[]> UnknownAsync(int release) => ReadAsync($"SELECT issue_id || '|' || unknown_bills FROM release_issue_party_lineups WHERE release_number = {release} ORDER BY issue_id");

    private Task<string[]> BaseAsync(int release) => ReadAsync($"SELECT issue_id || '|' || bills FROM release_issues WHERE release_number = {release} ORDER BY issue_id");

    private Task<string[]> BaseWithCongressAsync(int release) => ReadAsync($"SELECT issue_id || '|' || count_congress || '|' || bills FROM release_issues WHERE release_number = {release} AND issue_id = 'health'");

    private async Task<string[]> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }
}
