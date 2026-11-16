using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

/// <summary>
/// The officials views against the rules of the release, on the same rows. Each test seeds rows, reads the view as the public role,
/// and compares the numbers with the counts that the test states from its seeded casts, or with <see cref="PublishedAction.ToEntry"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiOfficialViewsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Role = "civic_lens_public";
    private const string LoginPassword = "test-only-password";
    private static readonly DateTimeOffset ReleaseTime = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] Substantive = [.. Enum.GetValues<PublishedVoteKind>().Where(kind => kind.IsSubstantive()).Select(kind => kind.ToString())];

    // Votes of the world. Person pN is the official at index N of the casts. Vote b is a rule, so it never counts toward the issue counts.
    private static readonly SeededVote[] Votes =
    [
        new('a', "2026-02-01") { Casts = ["Yea", "Not Voting", "Nay", "Present", "Aye"], Issues = ["health"], RollCall = 1 },
        new('b', "2026-02-03") { Casts = ["Yea", "Yea", "Not Voting", "Yea", "Yea"], Issues = ["health", "tax"], Kind = "Rule", RollCall = 2 },
        new('c', "2026-02-05") { Casts = ["Nay", "Yea", "Yea", "Not Voting", "Nay"], Issues = ["tax"], Chamber = "Senate", RollCall = 3 },
        new('d', "2024-05-01") { Casts = ["Yea", "Yea", "Yea", "Yea", "Yea"], Issues = ["health"], Congress = 118, RollCall = 4 }
    ];

    private string database = null!;

    public async Task InitializeAsync()
    {
        database = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ExecuteAsync(database, $"ALTER ROLE {Role} LOGIN PASSWORD '{LoginPassword}'");
        await ViewsSeed.ActivateReleaseAsync(database, 1, ReleaseTime);
    }

    public async Task DisposeAsync()
    {
        await ViewsSeed.ExecuteAsync(database, $"ALTER ROLE {Role} NOLOGIN PASSWORD NULL");
        await postgres.DropDatabaseAsync(database);
    }

    // Held counts the roll calls of the chamber in the Congress, and missed counts the Not Voting casts. Present is a cast.
    private static readonly string[][] Attendance =
    [
        ["House 119 2 0 2026-02-01 2026-02-03", "Senate 119 1 0 2026-02-05 2026-02-05"],
        ["House 119 2 1 2026-02-01 2026-02-03", "Senate 119 1 0 2026-02-05 2026-02-05"],
        ["House 119 2 1 2026-02-01 2026-02-03", "Senate 119 1 0 2026-02-05 2026-02-05"],
        ["House 119 2 0 2026-02-01 2026-02-03", "Senate 119 1 1 2026-02-05 2026-02-05"],
        ["House 119 2 0 2026-02-01 2026-02-03", "Senate 119 1 0 2026-02-05 2026-02-05"]
    ];

    // Only votes a and c count: vote b is a rule and vote d is of Congress 118. A Not Voting cast never counts, and Present does.
    private static readonly string[][] VoteIssueCounts = [["health 1", "tax 1"], ["tax 1"], ["health 1", "tax 1"], ["health 1"], ["health 1", "tax 1"]];

    [Fact]
    public async Task AttendanceCountsTheRollCallsHeldAndMissedForEachChamberAndCongress()
    {
        await ViewsSeed.AddVotesAsync(database, Votes);
        for (var index = 0; index < 5; index++)
        {
            var official = $"p{index}";
            var expected = Attendance[index];
            var actual = await QueryAsync($"""
                SELECT chamber || ' ' || congress || ' ' || held || ' ' || missed || ' ' || to_char(first_voted_on, 'YYYY-MM-DD') || ' ' || to_char(last_voted_on, 'YYYY-MM-DD')
                  FROM public_api.official_attendance WHERE official_id = '{official}' AND congress = 119 ORDER BY chamber
                """);
            Assert.Equal(expected, actual);
        }
        Assert.Equal(["House 118 1 0"], await QueryAsync("SELECT chamber || ' ' || congress || ' ' || held || ' ' || missed FROM public_api.official_attendance WHERE official_id = 'p0' AND congress = 118"));
    }

    [Fact]
    public async Task VoteIssueCountsEqualTheCastSubstantiveVotesOfTheOfficialBecauseOnlyACastSubstantiveVoteCounts()
    {
        await ViewsSeed.AddVotesAsync(database, Votes);
        for (var index = 0; index < 5; index++)
        {
            var expected = VoteIssueCounts[index];
            var actual = await QueryAsync($"""
                SELECT issue_id || ' ' || sum(records) FROM public_api.official_issue_counts
                 WHERE official_id = 'p{index}' AND congress = 119 AND source = 'vote' AND vote_kind = ANY(ARRAY['{string.Join("','", Substantive)}'])
                 GROUP BY issue_id ORDER BY issue_id COLLATE "C"
                """);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task BillIssueCountsEqualTheSponsoredBillEntriesBecauseOnlySponsoredBillsOfATrackedIssueCount()
    {
        var bills = new[]
        {
            new SeededBill('1', "2026-03-02") { BillNumber = 1, Issues = ["health"], Sponsorships = [new("p0", "Sponsor", "2026-01-10"), new("p1", "Cosponsor", "2026-01-10"), new("p2", "Cosponsor", "2026-01-11", "2026-02-01", false)] },
            new SeededBill('2', "2026-03-03") { BillNumber = 2, Issues = ["health", "tax"], Sponsorships = [new("p1", "Sponsor", "2026-03-03"), new("p1", "Cosponsor", "2026-03-03"), new("p0", "Cosponsor", "2026-03-03", null, false)] },
            new SeededBill('3', "2026-03-04") { BillNumber = 3, Issues = ["tax"], Sponsorships = [new("p0", "Sponsor", "2026-03-04")] },
            new SeededBill('4', "2024-05-01") { BillNumber = 4, Congress = 118, Issues = ["health"], Sponsorships = [new("p0", "Sponsor", "2024-05-01")] }
        };
        await BillsSeed.AddBillsAsync(database, bills);
        var entries = bills.Select(ActionOf).Select(action => action.ToEntry()).ToArray();
        foreach (var official in new[] { "p0", "p1", "p2" })
        {
            var named = entries.Where(entry => entry.Officials.Any(person => person.Id == official)).ToArray();
            var sponsored = named.Where(entry => entry.Kind == PublishedRecordKind.Action && ReleaseRecordOrder.CongressOf(entry) == 119).ToArray();
            foreach (var issue in new[] { "health", "tax" })
                Assert.Equal([sponsored.Count(entry => entry.IssueIds.Contains(issue)).ToString(CultureInfo.InvariantCulture)], await QueryAsync($"""
                    SELECT coalesce(sum(records), 0) FROM public_api.official_issue_counts
                     WHERE official_id = '{official}' AND congress = 119 AND source = 'bill' AND issue_id = '{issue}'
                    """));
        }
    }

    [Fact]
    public async Task RecordCountEqualsTheEntriesThatNameTheOfficialAndCountsNoRecordOfAnotherRelease()
    {
        await OfficialsSeed.AddOfficialsAsync(database, new SeededOfficial("p0", "Ann Able"), new SeededOfficial("p1", "Ben Baker"), new SeededOfficial("p2", "Cy Cole"));
        SeededEntry[] entries =
        [
            new("statement", 'a', "p0", "p1"), new("statement", 'b', "p0"), new("executiveAction", 'c', "p0") { DocumentType = "ExecutiveOrder" },
            new("statement", 'e', "p1") { FirstRelease = 2 }
        ];
        await OfficialsSeed.AddEntriesAsync(database, entries);
        PublishedRecordEntry[] manifest = [.. entries.Where(entry => entry is { FirstRelease: 1, LastRelease: null }).Select(ManifestEntry)];
        var expected = new[] { "p0", "p1", "p2" }.Select(id => $"{id} {manifest.Count(entry => entry.Officials.Any(official => official.Id == id))}").ToArray();
        await StoredBillCounts.RefreshRecordCountsAsync(database, 1);

        Assert.Equal(expected, await QueryAsync("SELECT official_id || ' ' || record_count FROM public_api.officials ORDER BY official_id"));
        Assert.Equal(["p0 3", "p1 1", "p2 0"], expected);
    }

    [Fact]
    public async Task TheSortNameIsTheStoredRosterLastNameAndANameWithNoStoredSortNameSortsByTheName()
    {
        await OfficialsSeed.AddOfficialsAsync(database, new SeededOfficial("p0", "Chris Van Hollen") { SortName = "Van Hollen" }, new SeededOfficial("p1", "Mia Vance"));

        Assert.Equal(["p0 Van Hollen", "p1 Mia Vance"], await QueryAsync("SELECT official_id || ' ' || sort_name FROM public_api.officials ORDER BY official_id"));
    }

    // The release entry counts a sponsored bill in the Congress of its last event date. The view counts it in the Congress of the bill number.
    [Fact]
    public async Task ABillIssueCountIsInTheCongressOfItsBillNumberAndNotInTheCongressOfItsLastSponsorshipEvent()
    {
        await BillsSeed.AddBillsAsync(database,
            new SeededBill('1', "2025-03-01") { BillNumber = 1, Congress = 118, Issues = ["health"], Sponsorships = [new("p0", "Sponsor", "2023-02-01"), new("p1", "Cosponsor", "2023-02-02"), new("p2", "Cosponsor", "2025-03-01")] });

        Assert.Equal(["118 health 1"], await QueryAsync("SELECT congress || ' ' || issue_id || ' ' || records FROM public_api.official_issue_counts WHERE official_id = 'p0' AND source = 'bill'"));
    }


    [Fact]
    public async Task ExecutiveActionCountsFollowTheDocumentTypeOfTheActiveEntriesAndLeaveOutOtherKindsAndOtherReleases()
    {
        SeededEntry[] entries =
        [
            new("executiveAction", 'a', "p0") { DocumentType = "ExecutiveOrder" }, new("executiveAction", 'b', "p0", "p1") { DocumentType = "ExecutiveOrder" },
            new("executiveAction", 'c', "p0") { DocumentType = "Proclamation" }, new("executiveAction", 'd', "p0") { DocumentType = "Notice", FirstRelease = 2 },
            new("statement", 'e', "p0")
        ];
        await OfficialsSeed.AddEntriesAsync(database, entries);
        var manifest = entries.Where(entry => entry is { Kind: "executiveAction", FirstRelease: 1 }).Select(ManifestEntry).ToArray();
        var expected = manifest.Where(entry => entry.Officials.Any(official => official.Id == "p0")).GroupBy(entry => entry.DocumentType!.Value.ToString())
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}").ToArray();

        Assert.Equal(expected, await QueryAsync("SELECT document_type || ' ' || actions FROM public_api.official_executive_actions WHERE official_id = 'p0' ORDER BY document_type COLLATE \"C\""));
        Assert.Equal(["ExecutiveOrder 1"], await QueryAsync("SELECT document_type || ' ' || actions FROM public_api.official_executive_actions WHERE official_id = 'p1'"));
    }

    [Fact]
    public async Task OnlyTheActiveReleaseShowsAndARollbackMovesTheOfficialsViewAtOnce()
    {
        await OfficialsSeed.AddOfficialsAsync(database, new SeededOfficial("p0", "Ann Able"));
        await OfficialsSeed.AddEntriesAsync(database, new SeededEntry("statement", 'a', "p0"));
        await ViewsSeed.ExecuteAsync(database, """
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (2, '000002-00000000000000000000000000000002', 'test', 2, 0, false);
            INSERT INTO release_officials (release_number, official_id, name) VALUES (2, 'p9', 'Dee Dole');
            """);

        Assert.Equal(["p0"], await QueryAsync("SELECT official_id FROM public_api.officials"));
        await ViewsSeed.ExecuteAsync(database, "UPDATE publication_releases SET is_active = (release_number = 2)");
        Assert.Equal(["p9 0"], await QueryAsync("SELECT official_id || ' ' || record_count FROM public_api.officials"));
    }

    [Fact]
    public async Task NoOfficialsViewShowsAPersonOrBioguideIdBecausePublicContractsKeyOfficialsByOurId()
    {
        await using var connection = await OpenPublicAsync();
        foreach (var view in new[] { "officials", "official_attendance", "official_party_lines", "official_issue_counts", "official_executive_actions" })
        {
            await AssertDeniedAsync(connection, $"SELECT person_id FROM public_api.{view}");
            await AssertDeniedAsync(connection, $"SELECT bioguide_id FROM public_api.{view}");
        }
        await AssertDeniedAsync(connection, "SELECT official_id FROM public.published_entry_officials", "42501");
    }

    private static PublishedAction ActionOf(SeededBill bill) => new()
    {
        RecordId = bill.RecordId,
        RevisionNumber = 1,
        Headline = "H",
        Summary = "S",
        TextOrigin = PublishedTextOrigin.Model,
        Congress = bill.Congress,
        BillType = bill.BillType,
        BillNumber = bill.BillNumber,
        BillTitle = "T",
        TrackedIssueIds = bill.Issues,
        Sponsorships = [.. bill.Sponsorships.Select(item => new PublishedSponsorship
        {
            Official = new PublishedOfficial { Id = item.Person, Name = item.Person },
            Role = Enum.Parse<PublishedSponsorshipRole>(item.Role),
            IsOriginal = item.IsOriginal,
            SponsoredOn = DateOnly.Parse(item.SponsoredOn, CultureInfo.InvariantCulture),
            WithdrawnOn = item.WithdrawnOn is null ? null : DateOnly.Parse(item.WithdrawnOn, CultureInfo.InvariantCulture),
            Citations = []
        })],
        Sources = [new PublishedDocumentVersion { ExtractionId = "e", Url = "https://example.gov/", ObservedAtUtc = ReleaseTime, Text = "t" }],
        ApprovedAtUtc = ReleaseTime,
        FirstPublishedAtUtc = ReleaseTime,
        Corrections = []
    };

    private static PublishedRecordEntry ManifestEntry(SeededEntry entry) => new()
    {
        Kind = entry.Kind == "executiveAction" ? PublishedRecordKind.ExecutiveAction : PublishedRecordKind.Statement,
        RecordId = entry.RecordId,
        RevisionNumber = 1,
        Headline = "H",
        FirstPublishedAtUtc = ReleaseTime,
        Officials = [.. entry.Officials.Select(id => new PublishedOfficial { Id = id, Name = id })],
        IssueIds = [],
        PolicyAreaIds = [],
        DocumentType = entry.DocumentType is null ? null : Enum.Parse<PublishedExecutiveActionKind>(entry.DocumentType)
    };

    private async Task<NpgsqlConnection> OpenPublicAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(database) { Username = Role, Password = LoginPassword };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<string[]> QueryAsync(string sql)
    {
        await using var connection = await OpenPublicAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        return [.. rows];
    }

    private static async Task AssertDeniedAsync(NpgsqlConnection connection, string sql, string sqlState = "42703")
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var error = await Assert.ThrowsAnyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(sqlState, error.SqlState);
    }
}
