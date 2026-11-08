using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Home;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Home;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class HomeEndpointsTests(PostgresCollection postgres)
{
    private static readonly DateTimeOffset Published = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);

    private async Task<ApiHost> StartAsync(SeededVote[] votes, SeededBill[]? bills = null, SeededBrief[]? briefs = null, SeededExecutiveAction[]? executiveActions = null)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        try
        {
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, Published);
            await ViewsSeed.AddVotesAsync(host.OperationalDatabase, votes);
            await BillsSeed.AddBillsAsync(host.OperationalDatabase, bills ?? []);
            await HomeSeed.AddBriefsAsync(host.OperationalDatabase, briefs ?? []);
            await HomeSeed.AddExecutiveActionsAsync(host.OperationalDatabase, executiveActions ?? []);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    [Fact]
    public async Task FreshnessUsesNewestPublishedRollCallEvenWhenItIsNotSubstantiveAndOmitsAnEmptyChamber()
    {
        var substantive = House('a', "2026-05-01", 1) with { Kind = "Amendment", BillType = null, BillNumber = null };
        var procedural = new SeededVote('b', "2026-05-31") { Kind = "Rule", RollCall = 2 };
        await using var host = await StartAsync([substantive, procedural]);

        var answer = await GetAsync(host);
        Assert.Equal("2026-05-31", answer.GetProperty("newestHouseRollCall").GetString());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("newestSenateRollCall").ValueKind);
        Assert.Equal(substantive.RecordId, answer.GetProperty("votes")[0].GetProperty("recordId").GetString());

        await using var dataSource = NpgsqlDataSource.Create(host.Options.PublicViewsDatabase!);
        var page = await new HomePageReader(new HomeStore(dataSource), null).ReadAsync(CancellationToken.None);
        Assert.NotNull(page);
        Assert.Equal(new DateOnly(2026, 5, 31), page.NewestHouseRollCall);
        Assert.Null(page.NewestSenateRollCall);
    }

    [Fact]
    public async Task NoActiveReleaseHasNoHomeAnswerOrFreshnessDates()
    {
        await using var host = await StartAsync([House('a', "2026-05-01", 1)]);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE publication_releases SET is_active = false");
        await using var dataSource = NpgsqlDataSource.Create(host.Options.PublicViewsDatabase!);

        Assert.Null(await new HomeStore(dataSource).ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExecutiveGroupsCountActivePublishedKindsAndChooseNewestSignedRecordWithRecordIdTieOrder()
    {
        var votes = new[] { House('1', "2026-05-01", 1), House('2', "2026-05-02", 2), House('3', "2026-05-03", 3) };
        var actions = new[]
        {
            new SeededExecutiveAction('a', "ExecutiveOrder", "2026-05-20", "2026-05-01") { Title = "Pinned active title", ActiveRevision = 2, AddUnpublishedRevision = true },
            new SeededExecutiveAction('b', "ExecutiveOrder", "2026-05-20", "2026-06-01") { Title = "Tie title" },
            new SeededExecutiveAction('c', "ExecutiveOrder", "2026-05-19", "2026-06-02") { Title = "Older signed title" },
            new SeededExecutiveAction('d', "Proclamation", "2026-05-18", "2026-06-03"),
            new SeededExecutiveAction('e', "ExecutiveOrder", "2026-05-21", "2026-05-22") { Title = "Unpublished wrong title", FirstRelease = 2 }
        };
        await using var host = await StartAsync(votes, briefs: [new SeededBrief('x', "2026-05-25")], executiveActions: actions);

        var answer = await GetAsync(host);
        var groups = answer.GetProperty("executiveActions").EnumerateArray().ToArray();

        Assert.Equal(2, groups.Length);
        var order = Assert.Single(groups, group => group.GetProperty("kind").GetString() == "executiveOrder");
        Assert.Equal(3, order.GetProperty("count").GetInt32());
        Assert.Equal(actions[0].RecordId, order.GetProperty("newestRecordId").GetString()!);
        Assert.Equal("Pinned active title", order.GetProperty("newestTitle").GetString());
        Assert.Equal("2026-05-20", order.GetProperty("newestSignedOn").GetString());
        var proclamation = Assert.Single(groups, group => group.GetProperty("kind").GetString() == "proclamation");
        Assert.Equal(1, proclamation.GetProperty("count").GetInt32());
        Assert.Equal(actions[3].RecordId, proclamation.GetProperty("newestRecordId").GetString()!);
        Assert.Equal(votes[2].RecordId, FeaturedId(answer));
        Assert.Equal([votes[1].RecordId, votes[0].RecordId], Ids(answer));
        Assert.Equal(new SeededBrief('x', "2026-05-25").RecordId, answer.GetProperty("brief").GetProperty("recordId").GetString()!);

        await using var dataSource = NpgsqlDataSource.Create(host.Options.PublicViewsDatabase!);
        var page = await new HomePageReader(new HomeStore(dataSource), null).ReadAsync(CancellationToken.None);
        Assert.NotNull(page);
        Assert.Equal(2, page.ExecutiveActions.Length);
        var pageOrder = Assert.Single(page.ExecutiveActions, group => group.Kind == PublishedExecutiveActionKind.ExecutiveOrder);
        Assert.Equal(3, pageOrder.Count);
        Assert.Equal(actions[0].RecordId, pageOrder.NewestRecordId);
        Assert.Equal("Pinned active title", pageOrder.NewestTitle);
        Assert.Equal(new DateOnly(2026, 5, 20), pageOrder.NewestSignedOn);
        var pageProclamation = Assert.Single(page.ExecutiveActions, group => group.Kind == PublishedExecutiveActionKind.Proclamation);
        Assert.Equal(1, pageProclamation.Count);
        Assert.Equal(actions[3].RecordId, pageProclamation.NewestRecordId);
        Assert.DoesNotContain(page.ExecutiveActions, group => group.NewestRecordId == actions[4].RecordId);

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, Published.AddDays(1));
        var next = await GetAsync(host);
        var nextOrder = Assert.Single(next.GetProperty("executiveActions").EnumerateArray(), group => group.GetProperty("kind").GetString() == "executiveOrder");
        Assert.Equal(2, next.GetProperty("release").GetInt32());
        Assert.Equal(4, nextOrder.GetProperty("count").GetInt32());
        Assert.Equal(actions[4].RecordId, nextOrder.GetProperty("newestRecordId").GetString());

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, Published);
        var rolledBack = await GetAsync(host);
        var restoredOrder = Assert.Single(rolledBack.GetProperty("executiveActions").EnumerateArray(), group => group.GetProperty("kind").GetString() == "executiveOrder");
        Assert.Equal(3, restoredOrder.GetProperty("count").GetInt32());
        Assert.Equal("Pinned active title", restoredOrder.GetProperty("newestTitle").GetString());
    }

    [Fact]
    public async Task ExecutiveOnlyReleaseReturnsGroupsAndAnEmptyVoteArray()
    {
        var action = new SeededExecutiveAction('a', "Memorandum", "2026-05-20", "2026-05-21");
        await using var host = await StartAsync([], executiveActions: [action]);

        var answer = await GetAsync(host);

        Assert.Equal(1, answer.GetProperty("release").GetInt32());
        Assert.Equal(0, answer.GetProperty("votes").GetArrayLength());
        Assert.Equal(1, answer.GetProperty("executiveActions")[0].GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task HomeIssueCountsUseReleaseCongressSubstantiveKindsAndActiveRevisionsIncludingZeroIssues()
    {
        var votes = new[]
        {
            new SeededVote('a', "2026-05-01") { Congress = 119, Issues = ["health"] },
            new SeededVote('b', "2026-05-02") { Congress = 119, Kind = "Rule", Issues = ["health"] },
            new SeededVote('c', "2026-05-03") { Congress = 118, Issues = ["health"] },
            new SeededVote('d', "2026-05-04") { Congress = 119, Chamber = "Senate", Kind = "Nomination", ActiveRevision = 2, EarlierIssues = ["health"], Issues = ["climate"] },
            new SeededVote('e', "2026-05-05") { Congress = 119, Issues = ["health"], FirstRelease = 2 }
        };
        var bills = new[]
        {
            new SeededBill('a', "2026-05-01") { Congress = 119, Issues = ["health"] },
            new SeededBill('b', "2026-05-02") { Congress = 118, Issues = ["health"] },
            new SeededBill('c', "2026-05-03") { Congress = 119, Issues = ["health"], FirstRelease = 2 }
        };
        await using var host = await StartAsync(votes, bills);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, """
            INSERT INTO release_issues (release_number, issue_id, name, definition)
            VALUES (1, 'health', 'Health', 'Health policy'), (1, 'climate', 'Climate', 'Climate policy'),
                   (1, 'empty', 'Empty issue', 'No records');
            """);

        var answer = await GetAsync(host);
        var issues = answer.GetProperty("issues").EnumerateArray().ToArray();
        Assert.Equal(3, issues.Length);
        var health = Assert.Single(issues, issue => issue.GetProperty("issueId").GetString() == "health");
        Assert.Equal(119, health.GetProperty("congress").GetInt32());
        Assert.Equal(1, health.GetProperty("substantiveVotes").GetInt32());
        Assert.Equal(1, health.GetProperty("bills").GetInt32());
        var climate = Assert.Single(issues, issue => issue.GetProperty("issueId").GetString() == "climate");
        Assert.Equal(1, climate.GetProperty("substantiveVotes").GetInt32());
        Assert.Equal(0, climate.GetProperty("bills").GetInt32());
        var empty = Assert.Single(issues, issue => issue.GetProperty("issueId").GetString() == "empty");
        Assert.Equal(0, empty.GetProperty("substantiveVotes").GetInt32());
        Assert.Equal(0, empty.GetProperty("bills").GetInt32());

        await using var dataSource = NpgsqlDataSource.Create(host.Options.PublicViewsDatabase!);
        var page = await new HomePageReader(new HomeStore(dataSource), null).ReadAsync(CancellationToken.None);
        Assert.NotNull(page);
        Assert.Equal(1, page.Release);
        Assert.Equal(3, page.Issues.Length);
        var pageHealth = Assert.Single(page.Issues, issue => issue.IssueId == "health");
        Assert.Equal("Health", pageHealth.Name);
        Assert.Equal(119, pageHealth.Congress);
        Assert.Equal(1, pageHealth.SubstantiveVotes);
        Assert.Equal(1, pageHealth.Bills);
        var pageClimate = Assert.Single(page.Issues, issue => issue.IssueId == "climate");
        Assert.Equal(119, pageClimate.Congress);
        Assert.Equal(1, pageClimate.SubstantiveVotes);
        Assert.Equal(0, pageClimate.Bills);
        var pageEmpty = Assert.Single(page.Issues, issue => issue.IssueId == "empty");
        Assert.Equal(0, pageEmpty.SubstantiveVotes);
        Assert.Equal(0, pageEmpty.Bills);

        using var detail = await host.Client.GetAsync("/v1/issues/health");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var allCongresses = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(2, allCongresses.RootElement.GetProperty("substantiveVotes").GetInt32());
        Assert.Equal(2, allCongresses.RootElement.GetProperty("bills").GetInt32());
    }

    [Fact]
    public async Task HomeIssueCongressUsesPublicationDateAndTreatsJanuarySecondAsPreviousCongress()
    {
        await using var host = await StartAsync([House('a', "2026-05-01", 1)]);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health', 'Health policy')");
        foreach (var (date, expectedCongress) in new[] { (new DateTime(2027, 1, 2, 0, 0, 0, DateTimeKind.Utc), 119), (new DateTime(2027, 1, 3, 0, 0, 0, DateTimeKind.Utc), 120) })
        {
            await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE publication_releases SET published_at_utc_ticks = {date.Ticks} WHERE release_number = 1");
            var answer = await GetAsync(host);
            Assert.Equal(expectedCongress, answer.GetProperty("issues")[0].GetProperty("congress").GetInt32());
        }
    }

    [Fact]
    public async Task HomeIssueCountsOmitScopedRowsWhenReleasePredatesCongressCalendar()
    {
        await using var host = await StartAsync([House('a', "2026-05-01", 1)]);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, """
            INSERT INTO release_issues (release_number, issue_id, name, definition)
            VALUES (1, 'health', 'Health', 'Health policy');
            UPDATE publication_releases SET published_at_utc_ticks = 600000000000000000 WHERE release_number = 1;
            """);

        var answer = await GetAsync(host);

        Assert.Empty(answer.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task ReleaseActivationBetweenTheTwoQueriesCannotMixHighlightsAndIssueCounts()
    {
        const string key = "home-issue-snapshot";
        await using var host = await StartAsync([
            new SeededVote('a', "2026-05-01") { Issues = ["health"] },
            new SeededVote('b', "2026-05-02") { Issues = ["health"], FirstRelease = 2 }
        ]);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, Published.AddDays(1));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, """
            INSERT INTO release_issues (release_number, issue_id, name, definition)
            VALUES (1, 'health', 'Health before', 'Health policy'),
                   (2, 'health', 'Health after', 'Health policy');
            CREATE FUNCTION wait_for_home_issues() RETURNS boolean LANGUAGE plpgsql
                SECURITY DEFINER SET search_path = pg_catalog AS $function$
            BEGIN
                PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('home-issue-snapshot', 0));
                RETURN true;
            END;
            $function$;
            CREATE OR REPLACE VIEW public_api.release_issues WITH (security_barrier = true) AS
            SELECT i.release_number, i.issue_id, i.name, i.definition
              FROM public.release_issues i JOIN public.publication_releases r ON r.release_number = i.release_number
             WHERE r.is_active AND public.wait_for_home_issues();
            """);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, Published);
        await using var held = await HeldAdvisoryLock.AcquireAsync(host.OperationalDatabase, key);

        var reading = GetAsync(host);
        await HeldAdvisoryLock.WaitForWaiterAsync(host.OperationalDatabase, key);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, Published.AddDays(1));
        await held.DisposeAsync();
        var before = await reading;
        var after = await GetAsync(host);

        Assert.Equal(1, before.GetProperty("release").GetInt32());
        Assert.Equal("Health before", before.GetProperty("issues")[0].GetProperty("name").GetString());
        Assert.Equal(1, before.GetProperty("issues")[0].GetProperty("substantiveVotes").GetInt32());
        Assert.Equal(2, after.GetProperty("release").GetInt32());
        Assert.Equal("Health after", after.GetProperty("issues")[0].GetProperty("name").GetString());
        Assert.Equal(2, after.GetProperty("issues")[0].GetProperty("substantiveVotes").GetInt32());
    }

    private static async Task<JsonElement> GetAsync(ApiHost host, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync("/v1/home");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"/v1/home gave {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string[] Ids(JsonElement answer) =>
        [.. answer.GetProperty("votes").EnumerateArray().Select(vote => vote.GetProperty("recordId").GetString()!)];

    private static string? FeaturedId(JsonElement answer) =>
        answer.GetProperty("featuredVote").ValueKind == JsonValueKind.Null ? null : answer.GetProperty("featuredVote").GetProperty("recordId").GetString();

    private static SeededVote House(char id, string day, int rollCall) => new(id, day) { RollCall = rollCall };

    private static SeededVote Senate(char id, string day, int rollCall) => new(id, day) { Chamber = "Senate", RollCall = rollCall, Result = "Agreed to" };

    private static bool HasCounts(SeededVote vote) => vote.Casts.Any(cast => cast is "Yea" or "Aye" or "Nay" or "No");

    /// <summary>The entry as the publisher writes it. <c>PublishedVote</c> gives an alignment only to a substantive vote with counts, and <paramref name="bothParties"/> says
    /// whether the vote also has a Democrat row and a Republican row. A party with no majority still gets the mixed alignment. Another vote has none, so the entry carries no counts and no alignment.</summary>
    private static PublishedRecordEntry EntryOf(SeededVote vote, bool bothParties = true) => new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = vote.RecordId,
        RevisionNumber = 1,
        Headline = $"Headline {vote.Id}",
        FirstPublishedAtUtc = Published,
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = [],
        EventDate = DateOnly.Parse(vote.VotedOn, System.Globalization.CultureInfo.InvariantCulture),
        YeaCount = HasCounts(vote) ? vote.Casts.Count(cast => cast is "Yea" or "Aye") : null,
        NayCount = HasCounts(vote) ? vote.Casts.Count(cast => cast is "Nay" or "No") : null,
        Alignment = Enum.Parse<PublishedVoteKind>(vote.Kind).IsSubstantive() && HasCounts(vote) && bothParties ? PublishedVoteAlignment.PartiesAgree : null,
        Chamber = Enum.Parse<PublishedChamber>(vote.Chamber),
        VoteKind = Enum.Parse<PublishedVoteKind>(vote.Kind)
    };

    private static PublicationRelease ReleaseOf(IEnumerable<PublishedRecordEntry> entries) => new()
    {
        SchemaVersion = PublicationProtocol.SchemaVersion,
        ReleaseNumber = 1,
        PublishedAtUtc = Published,
        Withdrawals = [],
        Records = [.. entries]
    };

    private static string[] StaticVotePick(IEnumerable<PublishedRecordEntry> entries) =>
        [.. HomeHighlights.From(ReleaseOf(entries), [], 0).Votes.Select(entry => entry.RecordId)];

    [Fact]
    public async Task TheFeaturedVoteIsSeparateFromTwoRecentVotesPerChamber()
    {
        var votes = new[]
        {
            House('1', "2026-05-01", 1), House('2', "2026-05-02", 2), House('3', "2026-05-03", 3), House('4', "2026-05-05", 4),
            Senate('5', "2026-05-04", 5), Senate('6', "2026-05-06", 6), Senate('7', "2026-04-01", 7),
            // Newer than the picks but not substantive, and newer with candidate names only: neither may appear.
            new SeededVote('8', "2026-05-30") { Kind = "Rule", RollCall = 8 },
            new SeededVote('9', "2026-05-31") { RollCall = 9, Kind = "Other", Casts = ["Smith", "Jones"] }
        };
        await using var host = await StartAsync(votes);

        var answer = await GetAsync(host);

        Assert.Equal(votes[5].RecordId, FeaturedId(answer));
        Assert.Equal([votes[3].RecordId, votes[4].RecordId, votes[2].RecordId, votes[6].RecordId], Ids(answer));
        Assert.Equal(4, Ids(answer).Length);
    }

    [Fact]
    public async Task RecentVotesStillShowWhenNoPassageVoteNamesABill()
    {
        SeededVote[] votes =
        [
            House('a', "2026-05-01", 1) with { Kind = "Amendment" },
            Senate('b', "2026-05-02", 2) with { Kind = "Nomination", BillType = null, BillNumber = null },
            House('c', "2026-05-03", 3) with { BillType = null, BillNumber = null }
        ];
        await using var host = await StartAsync(votes);

        var answer = await GetAsync(host);

        Assert.Null(FeaturedId(answer));
        Assert.Equal([votes[2].RecordId, votes[1].RecordId, votes[0].RecordId], Ids(answer));
    }

    [Fact]
    public async Task RecentVotesKeepThePublishedAlignmentInTheApiAndHomePage()
    {
        var votes = new[]
        {
            House('a', "2026-05-01", 1), House('b', "2026-05-02", 2),
            Senate('c', "2026-05-03", 3), Senate('d', "2026-05-04", 4), House('e', "2026-05-05", 5)
        };
        await using var host = await StartAsync(votes);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            UPDATE vote_revisions SET alignment = CASE record_id
                WHEN '{votes[0].RecordId}' THEN 'Split'
                WHEN '{votes[1].RecordId}' THEN 'PartiesAgree'
                WHEN '{votes[2].RecordId}' THEN 'Mixed'
                ELSE NULL END, columns_written = true
            WHERE revision_number = 1;
            INSERT INTO vote_revisions
            SELECT (jsonb_populate_record(NULL::public.vote_revisions,
                to_jsonb(v) || jsonb_build_object('revision_number', 2, 'alignment', 'Mixed'))).*
            FROM vote_revisions v WHERE record_id = '{votes[0].RecordId}' AND revision_number = 1;
            """);

        var answer = await GetAsync(host);
        var servedVotes = answer.GetProperty("votes").EnumerateArray().ToDictionary(
            vote => vote.GetProperty("recordId").GetString()!, vote => vote.GetProperty("alignment"));

        Assert.Equal("split", servedVotes[votes[0].RecordId].GetString());
        Assert.Equal("partiesAgree", servedVotes[votes[1].RecordId].GetString());
        Assert.Equal("mixed", servedVotes[votes[2].RecordId].GetString());
        Assert.Equal(JsonValueKind.Null, servedVotes[votes[3].RecordId].ValueKind);
        Assert.Equal(votes[4].RecordId, FeaturedId(answer));

        await using var dataSource = NpgsqlDataSource.Create(host.Options.PublicViewsDatabase!);
        var page = await new HomePageReader(new HomeStore(dataSource), null).ReadAsync(CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(PublishedVoteAlignment.Split, page.Votes.Single(vote => vote.RecordId == votes[0].RecordId).Alignment);
        Assert.Equal(PublishedVoteAlignment.PartiesAgree, page.Votes.Single(vote => vote.RecordId == votes[1].RecordId).Alignment);
        Assert.Equal(PublishedVoteAlignment.Mixed, page.Votes.Single(vote => vote.RecordId == votes[2].RecordId).Alignment);
        Assert.Null(page.Votes.Single(vote => vote.RecordId == votes[3].RecordId).Alignment);
    }

    [Fact]
    public async Task FeaturedVoteUsesLatestPassageOnBillTieOrderAndCarriesItsSummaryAndSourceLabelWithoutCountsFabricated()
    {
        var votes = new[]
        {
            House('a', "2026-05-01", 1), House('b', "2026-05-02", 2),
            new SeededVote('c', "2026-05-03") { RollCall = 3, Casts = ["Unknown"], TextOrigin = "Model" },
            new SeededVote('d', "2026-05-03") { RollCall = 4, Casts = ["Yea", "Nay"], Chamber = "Senate", Result = "Agreed to", TextOrigin = "Model" },
            new SeededVote('e', "2026-05-04") { RollCall = 5, Kind = "Rule" },
            new SeededVote('f', "2026-05-05") { RollCall = 6, BillType = null, BillNumber = null }
        };
        SeededBill[] bills = [new SeededBill('0', "2026-03-01") { BillNumber = 1, BillLine = "Current line." }];
        await using var host = await StartAsync(votes, bills);

        var answer = await GetAsync(host);
        var featured = answer.GetProperty("featuredVote");

        Assert.Equal(votes[2].RecordId, featured.GetProperty("recordId").GetString());
        Assert.Equal("Summary c", featured.GetProperty("summary").GetString());
        Assert.Equal("model", featured.GetProperty("textOrigin").GetString());
        Assert.Equal(PublishedRecordLabels.ModelWording, featured.GetProperty("label").GetString());
        Assert.Equal("Current line.", featured.GetProperty("billLine").GetString());
        Assert.Equal(PublishedWording.BillLineSource, featured.GetProperty("billLineSource").GetString());
        Assert.Equal(JsonValueKind.Null, featured.GetProperty("yea").ValueKind);
        Assert.DoesNotContain(votes[2].RecordId, Ids(answer));
        Assert.Contains(votes[3].RecordId, Ids(answer));
        Assert.Equal([votes[5].RecordId, votes[3].RecordId, votes[1].RecordId], Ids(answer));
    }

    [Fact]
    public async Task TheVotesDifferFromTheStaticPickForAVoteWithCountsAndNoAlignmentAndForVotesOfOneDay()
    {
        var votes = new[]
        {
            House('1', "2026-05-03", 3), House('2', "2026-05-05", 4), Senate('3', "2026-05-04", 5), Senate('4', "2026-05-06", 6),
            // Substantive with counts, but no Democrat row and no Republican row, so the entry has no alignment.
            House('a', "2026-06-01", 8)
        };
        await using var host = await StartAsync(votes);

        var answer = await GetAsync(host);

        var entries = votes.Select(vote => EntryOf(vote, bothParties: vote.Id != 'a'));
        Assert.Equal([votes[3].RecordId, votes[1].RecordId, votes[2].RecordId, votes[0].RecordId], StaticVotePick(entries));
        Assert.Equal([votes[3].RecordId, votes[1].RecordId, votes[2].RecordId, votes[0].RecordId], Ids(answer));
        // Two House votes of one day: the static pick orders them by record ID, the route by later roll call.
        var sameDay = new[] { House('c', "2026-04-20", 10), House('d', "2026-04-20", 11) };
        await using var tie = await StartAsync(sameDay);
        Assert.Equal([sameDay[0].RecordId, sameDay[1].RecordId], StaticVotePick(sameDay.Select(vote => EntryOf(vote))));
        var tieAnswer = await GetAsync(tie);
        Assert.Equal(sameDay[1].RecordId, FeaturedId(tieAnswer));
        Assert.Equal([sameDay[0].RecordId], Ids(tieAnswer));
    }

    [Fact]
    public async Task EachVoteCarriesItsCountsResultWordsAndLabelAndTheBillLineOfTheFirstBillRecordByRecordId()
    {
        var votes = new[] { House('1', "2026-05-01", 1), new SeededVote('2', "2026-05-02") { Chamber = "Senate", RollCall = 2, Result = "Agreed to", TextOrigin = "Person", Casts = ["Aye", "Aye", "No"], BillType = null, BillNumber = null } };
        var bills = new[]
        {
            new SeededBill('b', "2026-03-01") { BillNumber = 1, BillLine = "Second bill line." },
            new SeededBill('a', "2026-03-01") { BillNumber = 1, BillLine = "First bill line." }
        };
        await using var host = await StartAsync(votes, bills);

        var answer = await GetAsync(host);

        var senate = answer.GetProperty("votes")[0];
        Assert.Equal("Agreed to", senate.GetProperty("result").GetString());
        Assert.Equal(PlainVoteWords.Result("Agreed to"), senate.GetProperty("resultWords").GetString());
        Assert.Equal((2, 1), (senate.GetProperty("yea").GetInt32(), senate.GetProperty("nay").GetInt32()));
        Assert.Equal(JsonValueKind.Null, senate.GetProperty("label").ValueKind);
        Assert.Equal(JsonValueKind.Null, senate.GetProperty("billLine").ValueKind);
        Assert.Equal(JsonValueKind.Null, senate.GetProperty("billLineSource").ValueKind);
        var house = answer.GetProperty("featuredVote");
        Assert.Equal(PublishedRecordLabels.ModelWording, house.GetProperty("label").GetString());
        Assert.Equal("First bill line.", house.GetProperty("billLine").GetString());
        Assert.Equal(PublishedWording.BillLineSource, house.GetProperty("billLineSource").GetString());
        Assert.Equal((2, 1), (house.GetProperty("yea").GetInt32(), house.GetProperty("nay").GetInt32()));
    }

    [Fact]
    public async Task TheBillLineIsTheLineThatTheStaticBillLinesGiveWhenTheBillRecordsAreInTheActiveRelease()
    {
        var votes = new[]
        {
            new SeededVote('1', "2026-05-01") { Chamber = "Senate", RollCall = 1, BillNumber = 1 }, new SeededVote('2', "2026-05-02") { RollCall = 2, BillNumber = 2 },
            new SeededVote('3', "2026-05-03") { RollCall = 3, BillNumber = 3 }
        };
        var bills = new[]
        {
            // The first record of the bill has no line, so the line is null, as in the static rule.
            new SeededBill('a', "2026-03-01") { BillNumber = 1 }, new SeededBill('b', "2026-03-01") { BillNumber = 1, BillLine = "Line b." },
            new SeededBill('d', "2026-03-01") { BillNumber = 2, BillLine = "Line d." }, new SeededBill('c', "2026-03-01") { BillNumber = 2, BillLine = "Line c." },
            new SeededBill('0', "2026-03-01") { BillNumber = 3, BillLine = "No sponsorships.", Sponsorships = [] }
        };
        await using var host = await StartAsync(votes, bills);

        var answer = await GetAsync(host);

        var actions = bills.Select(bill => new PublishedRecordEntry
        {
            Kind = PublishedRecordKind.Action,
            RecordId = bill.RecordId,
            RevisionNumber = 1,
            Headline = "H",
            FirstPublishedAtUtc = Published,
            Officials = [],
            IssueIds = [],
            PolicyAreaIds = [],
            BillLine = bill.BillLine
        }).ToArray();
        var actionIdsByVote = votes.ToDictionary(vote => vote.RecordId,
            vote => bills.Where(bill => bill.BillNumber == vote.BillNumber).Select(bill => bill.RecordId).Order(StringComparer.Ordinal).ToImmutableArray());
        var lines = ReleaseBillLines.From(actions, actionIdsByVote);
        var expected = votes.Take(2).Reverse().Select(vote => lines.Of(EntryOf(vote))).ToArray();
        var served = answer.GetProperty("votes").EnumerateArray().Select(vote => vote.GetProperty("billLine").GetString()).ToArray();
        Assert.Equal(expected, served);
        Assert.Equal(new string?[] { "Line c.", null }, served);
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("featuredVote").GetProperty("billLine").ValueKind);
    }

    [Fact]
    public async Task VotesOfOneDayComeLaterRollCallFirstAndTheCardStopsAtTwoForEachChamber()
    {
        var votes = new[] { House('1', "2026-05-01", 1), House('2', "2026-05-01", 2), House('3', "2026-05-01", 3), Senate('4', "2026-05-01", 4) };
        await using var host = await StartAsync(votes);

        var answer = await GetAsync(host);

        Assert.Equal(votes[2].RecordId, FeaturedId(answer));
        Assert.Equal([votes[1].RecordId, votes[0].RecordId, votes[3].RecordId], Ids(answer));
    }

    [Fact]
    public async Task TheBriefIsTheNewestWeekOfTheActiveReleaseAndNullWhenNoBriefShows()
    {
        var briefs = new[]
        {
            new SeededBrief('a', "2026-05-04"), new SeededBrief('b', "2026-05-18") { TextOrigin = "Person" }, new SeededBrief('c', "2026-05-11"),
            new SeededBrief('d', "2026-05-25") { WithColumns = false }, new SeededBrief('e', "2026-06-01") { FirstRelease = 2 }
        };
        await using var withBriefs = await StartAsync([House('1', "2026-05-01", 1)], briefs: briefs);
        await using var without = await StartAsync([House('1', "2026-05-01", 1)]);

        var brief = (await GetAsync(withBriefs)).GetProperty("brief");
        var none = (await GetAsync(without)).GetProperty("brief");

        Assert.Equal(briefs[1].RecordId, brief.GetProperty("recordId").GetString());
        Assert.Equal("2026-05-18", brief.GetProperty("weekStart").GetString());
        Assert.Equal("Brief b", brief.GetProperty("headline").GetString());
        Assert.Equal(JsonValueKind.Null, brief.GetProperty("label").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.ValueKind);
    }

    private static PublishedRecordEntry BriefEntry(SeededBrief brief, DateTimeOffset firstPublished) => new()
    {
        Kind = PublishedRecordKind.Brief,
        RecordId = brief.RecordId,
        RevisionNumber = 1,
        Headline = brief.Headline,
        FirstPublishedAtUtc = firstPublished,
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = [],
        EventDate = DateOnly.Parse(brief.WeekStart, System.Globalization.CultureInfo.InvariantCulture)
    };

    private static string StaticBriefPick(IEnumerable<PublishedRecordEntry> entries) => ReleaseRecordOrder.BriefsNewestFirst(entries).First().RecordId;

    [Fact]
    public async Task TheBriefIsTheStaticHomeBriefWhenEveryBriefHasColumnsAndNoTwoShareAWeek()
    {
        var briefs = new[] { new SeededBrief('a', "2026-05-04"), new SeededBrief('b', "2026-05-18"), new SeededBrief('c', "2026-05-11") };
        await using var host = await StartAsync([], briefs: briefs);

        var brief = (await GetAsync(host)).GetProperty("brief");

        Assert.Equal(StaticBriefPick(briefs.Select(item => BriefEntry(item, Published))), brief.GetProperty("recordId").GetString());
        Assert.Equal(briefs[1].RecordId, brief.GetProperty("recordId").GetString());
    }

    [Fact]
    public async Task TheBriefDiffersFromTheStaticHomeBriefForOneWeekAndForARevisionThatHoldsOnlyJson()
    {
        // The static pick breaks a tie on the week by the later first publication, the route by record ID.
        // The static pick also counts a brief that an older code wrote with revision_json only. The view hides it.
        var same = new[] { new SeededBrief('x', "2026-06-01"), new SeededBrief('y', "2026-06-01") };
        var old = new SeededBrief('d', "2026-06-08") { WithColumns = false };
        await using var host = await StartAsync([], briefs: [.. same, old]);

        var brief = (await GetAsync(host)).GetProperty("brief");

        Assert.Equal(same[1].RecordId, StaticBriefPick([BriefEntry(same[0], Published), BriefEntry(same[1], Published.AddHours(1))]));
        Assert.Equal(old.RecordId, StaticBriefPick([BriefEntry(same[0], Published), BriefEntry(same[1], Published.AddHours(1)), BriefEntry(old, Published.AddHours(2))]));
        Assert.Equal(same[0].RecordId, brief.GetProperty("recordId").GetString());
    }

    [Fact]
    public async Task OnlyAModelBriefHasTheBriefLabelAndTheVoteLabelNeverShowsOnABrief()
    {
        await using var model = await StartAsync([], briefs: [new SeededBrief('a', "2026-05-04")]);
        await using var person = await StartAsync([], briefs: [new SeededBrief('a', "2026-05-04") { TextOrigin = "Person" }]);

        var modelBrief = (await GetAsync(model)).GetProperty("brief");
        var personBrief = (await GetAsync(person)).GetProperty("brief");

        Assert.Equal(PublishedWording.ModelBrief, modelBrief.GetProperty("label").GetString());
        Assert.NotEqual(PublishedRecordLabels.ModelWording, modelBrief.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, personBrief.GetProperty("label").ValueKind);
    }

    [Fact]
    public async Task NoAnswerSetsACacheHeaderBecauseTheOtherVoteRoutesSetNone()
    {
        await using var host = await StartAsync([House('1', "2026-05-01", 1)]);
        await using var empty = await ApiHost.StartAsync(postgres, new FakeReleaseSite());

        using var ok = await host.Client.GetAsync("/v1/home");
        using var none = await empty.Client.GetAsync("/v1/home");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Null(ok.Headers.CacheControl);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, none.StatusCode);
        Assert.Null(none.Headers.CacheControl);
    }

    [Fact]
    public async Task ANewServiceGives503AndAReleaseWithABriefAndNoVotesGivesAnEmptyVoteList()
    {
        await using var empty = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await GetAsync(empty, HttpStatusCode.ServiceUnavailable);
        await using var host = await StartAsync([], briefs: [new SeededBrief('a', "2026-05-04")]);

        var answer = await GetAsync(host);

        Assert.Equal(1, answer.GetProperty("release").GetInt32());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("featuredVote").ValueKind);
        Assert.Equal(0, answer.GetProperty("votes").GetArrayLength());
        Assert.Equal(0, answer.GetProperty("executiveActions").GetArrayLength());
    }
}
