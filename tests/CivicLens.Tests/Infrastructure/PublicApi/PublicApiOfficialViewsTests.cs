using System.Globalization;
using CivicLens.Application.Publication;
using AngleSharp.Html.Parser;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

/// <summary>
/// The officials views against the C# rules of the static pages, on the same rows. Each test seeds rows, reads the view as the public role,
/// and compares the numbers with <see cref="MissedVotes"/>, <see cref="TalkAndActionProfile"/>, <see cref="PublishedAction.ToEntry"/>, and the rendered officials card.
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

    [Fact]
    public async Task AttendanceEqualsMissedVotesOfTheStaticPageForEachChamberAndCongressBecauseOneRuleMustGiveBothAnswers()
    {
        await ViewsSeed.AddVotesAsync(database, Votes);
        for (var index = 0; index < 5; index++)
        {
            var official = $"p{index}";
            var expected = MissedVotes.Of(OfficialVotes(index).Where(vote => vote.Congress == 119))
                .Select(row => $"{row.Chamber} 119 {row.Held} {row.Missed} {row.First:yyyy-MM-dd} {row.Last:yyyy-MM-dd}").ToArray();
            var actual = await QueryAsync($"""
                SELECT chamber || ' ' || congress || ' ' || held || ' ' || missed || ' ' || to_char(first_voted_on, 'YYYY-MM-DD') || ' ' || to_char(last_voted_on, 'YYYY-MM-DD')
                  FROM public_api.official_attendance WHERE official_id = '{official}' AND congress = 119 ORDER BY chamber
                """);
            Assert.Equal(expected, actual);
        }
        Assert.Equal(["House 118 1 0"], await QueryAsync("SELECT chamber || ' ' || congress || ' ' || held || ' ' || missed FROM public_api.official_attendance WHERE official_id = 'p0' AND congress = 118"));
    }

    [Fact]
    public async Task VoteIssueCountsEqualTheVotesColumnOfTheProfileBecauseOnlyACastSubstantiveVoteCounts()
    {
        await ViewsSeed.AddVotesAsync(database, Votes);
        for (var index = 0; index < 5; index++)
        {
            var column = TalkAndActionProfile.Of(119, [], OfficialVotes(index).Where(vote => vote.Congress == 119)).Votes;
            var expected = column.RecordIdsByIssue.Select(issue => $"{issue.Key} {issue.Value.Length}").Order(StringComparer.Ordinal).ToArray();
            var actual = await QueryAsync($"""
                SELECT issue_id || ' ' || sum(records) FROM public_api.official_issue_counts
                 WHERE official_id = 'p{index}' AND congress = 119 AND source = 'vote' AND vote_kind = ANY(ARRAY['{string.Join("','", Substantive)}'])
                 GROUP BY issue_id ORDER BY issue_id COLLATE "C"
                """);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task BillIssueCountsEqualTheEntriesOfTheStaticPageBecauseOnlySponsoredBillsOfATrackedIssueCount()
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
            var profile = TalkAndActionProfile.Of(119, named, []);
            foreach (var issue in new[] { "health", "tax" })
                Assert.Equal([profile.SponsoredOn(issue).Length.ToString(CultureInfo.InvariantCulture)], await QueryAsync($"""
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

        Assert.Equal(expected, await QueryAsync("SELECT official_id || ' ' || record_count FROM public_api.officials ORDER BY official_id"));
        Assert.Equal(["p0 3", "p1 1", "p2 0"], expected);
    }

    // The static page counts a sponsored bill in the Congress of its last event date. The view counts it in the Congress of the bill number.
    [Fact]
    public async Task ABillIssueCountIsInTheCongressOfItsBillNumberAndNotInTheCongressOfItsLastSponsorshipEvent()
    {
        await BillsSeed.AddBillsAsync(database,
            new SeededBill('1', "2025-03-01") { BillNumber = 1, Congress = 118, Issues = ["health"], Sponsorships = [new("p0", "Sponsor", "2023-02-01"), new("p1", "Cosponsor", "2023-02-02"), new("p2", "Cosponsor", "2025-03-01")] },
            new SeededBill('2', "2026-03-01") { BillNumber = 2, Congress = 119, Sponsorships = [new("p0", "Sponsor", "2026-01-10")] });

        Assert.Equal(["118 health 1"], await QueryAsync("SELECT congress || ' ' || issue_id || ' ' || records FROM public_api.official_issue_counts WHERE official_id = 'p0' AND source = 'bill'"));
    }

    // The page picks the term. The test renders the officials page with the real renderer and reads the card, so the page rule stays the one owner.
    [Fact]
    public async Task TheLatestTermIsTheNewestTermStartedOnTheReleaseDayAsTheOfficialsCardChoosesIt()
    {
        SeededTerm[] terms =
        [
            new("Senator", "Senator", "2015-01-03") { State = "MA", Party = "Democrat", EndsOn = "2019-01-03" },
            new("Senator", "Senator", "2019-01-03") { State = "NY", Party = "Democrat", EndsOn = "2025-01-03" },
            new("Representative", "Representative", "2025-01-03") { State = "CA", District = 2, Party = "Democrat", EndsOn = "2027-01-03" },
            new("Representative", "Representative", "2025-01-03") { State = "TX", District = 3, Party = "Republican", EndsOn = "2027-01-03" },
            new("President", "Executive", "2026-06-03") { Party = "Other" }
        ];
        await OfficialsSeed.AddOfficialsAsync(database, new SeededOfficial("p0", "Ann Able", terms));
        var profile = new PublishedOfficialProfile { Id = "p0", Name = "Ann Able", Terms = [.. terms.Select(OfficeTerm)] };
        var html = await new ReleaseRenderer(null, null).RenderOfficialsAsync([new OfficialListing(profile, 0)], ReleaseTime, CancellationToken.None);
        var card = new HtmlParser().ParseDocument(html).QuerySelector("li.official-card")!;

        Assert.Equal("TX", card.GetAttribute("data-state"));
        Assert.Equal([$"{card.GetAttribute("data-office")} {card.GetAttribute("data-state")} Republican 2027-01-03"],
            await QueryAsync("SELECT lower(kind) || ' ' || state || ' ' || party || ' ' || to_char(term_end_on, 'YYYY-MM-DD') FROM public_api.officials"));
        Assert.Contains("Republican", card.TextContent, StringComparison.Ordinal);
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

    private static PublishedOfficialVote[] OfficialVotes(int index) =>
    [.. Votes.Select(vote => new PublishedOfficialVote
    {
        RecordId = vote.RecordId,
        VotedOn = DateOnly.ParseExact(vote.VotedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Chamber = Enum.Parse<PublishedChamber>(vote.Chamber),
        Congress = vote.Congress,
        Session = vote.Session,
        RollCallNumber = vote.RollCall,
        Cast = vote.Casts[index],
        Result = vote.Result,
        SplitVote = false,
        Party = "Democrat",
        Headline = "H",
        Kind = Enum.Parse<PublishedVoteKind>(vote.Kind),
        IssueIds = vote.Issues
    })];

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

    private static PublishedOfficeTerm OfficeTerm(SeededTerm term) => new()
    {
        Office = term.Office,
        Kind = Enum.Parse<PublishedOfficeKind>(term.Kind),
        State = term.State,
        Party = term.Party,
        Caucus = term.Caucus,
        Start = DateOnly.Parse(term.StartsOn, CultureInfo.InvariantCulture),
        End = term.EndsOn is null ? null : DateOnly.Parse(term.EndsOn, CultureInfo.InvariantCulture)
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
