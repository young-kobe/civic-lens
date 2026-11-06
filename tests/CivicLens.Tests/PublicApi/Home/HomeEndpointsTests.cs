using System.Net;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Home;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Home;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class HomeEndpointsTests(PostgresCollection postgres)
{
    private static readonly DateTimeOffset Published = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);

    private async Task<ApiHost> StartAsync(SeededVote[] votes, SeededBill[]? bills = null, SeededBrief[]? briefs = null)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, Published);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, votes);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, bills ?? []);
        await HomeSeed.AddBriefsAsync(host.OperationalDatabase, briefs ?? []);
        return host;
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

    private static SeededVote House(char id, string day, int rollCall) => new(id, day) { RollCall = rollCall };

    private static SeededVote Senate(char id, string day, int rollCall) => new(id, day) { Chamber = "Senate", RollCall = rollCall, Result = "Agreed to" };

    private static PublishedRecordEntry EntryOf(SeededVote vote) => new()
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
        YeaCount = vote.Casts.Count(cast => cast is "Yea" or "Aye"),
        NayCount = vote.Casts.Count(cast => cast is "Nay" or "No"),
        Alignment = PublishedVoteAlignment.PartiesAgree,
        Chamber = Enum.Parse<PublishedChamber>(vote.Chamber),
        VoteKind = Enum.Parse<PublishedVoteKind>(vote.Kind)
    };

    [Fact]
    public async Task TheVotesAreTheSameAsTheStaticHomeHighlightsPickBecauseOneRuleMustGiveBothAnswers()
    {
        var votes = new[]
        {
            House('1', "2026-05-01", 1), House('2', "2026-05-02", 2), House('3', "2026-05-03", 3), House('4', "2026-05-05", 4),
            Senate('5', "2026-05-04", 5), Senate('6', "2026-05-06", 6), Senate('7', "2026-04-01", 7),
            // Newer than the picks but not substantive, and newer with candidate names only: neither may appear.
            new SeededVote('8', "2026-05-30") { Kind = "Rule", RollCall = 8 },
            new SeededVote('9', "2026-05-31") { RollCall = 9, Casts = ["Smith", "Jones"] }
        };
        await using var host = await StartAsync(votes);

        var answer = await GetAsync(host);

        var release = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = 1,
            PublishedAtUtc = Published,
            Withdrawals = [],
            Records = [.. votes.Where(vote => vote.Casts.All(cast => cast is "Yea" or "Nay") && vote.Kind == "Passage").Select(EntryOf)]
        };
        var expected = HomeHighlights.From(release, [], 0).Votes.Select(entry => entry.RecordId);
        Assert.Equal(expected, Ids(answer));
        Assert.Equal(4, Ids(answer).Length);
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
        var house = answer.GetProperty("votes")[1];
        Assert.Equal(PublishedRecordLabels.ModelWording, house.GetProperty("label").GetString());
        Assert.Equal("First bill line.", house.GetProperty("billLine").GetString());
        Assert.Equal((2, 1), (house.GetProperty("yea").GetInt32(), house.GetProperty("nay").GetInt32()));
    }

    [Fact]
    public async Task VotesOfOneDayComeLaterRollCallFirstAndTheCardStopsAtTwoForEachChamber()
    {
        var votes = new[] { House('1', "2026-05-01", 1), House('2', "2026-05-01", 2), House('3', "2026-05-01", 3), Senate('4', "2026-05-01", 4) };
        await using var host = await StartAsync(votes);

        var answer = await GetAsync(host);

        Assert.Equal([votes[2].RecordId, votes[1].RecordId, votes[3].RecordId], Ids(answer));
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

    [Fact]
    public async Task AModelBriefHeadlineHasItsLabelBecauseModelTextNeverShowsWithoutOne()
    {
        await using var host = await StartAsync([House('1', "2026-05-01", 1)], briefs: [new SeededBrief('a', "2026-05-04")]);

        var brief = (await GetAsync(host)).GetProperty("brief");

        Assert.Equal(PublishedRecordLabels.ModelWording, brief.GetProperty("label").GetString());
    }

    [Fact]
    public async Task TheAnswerCanBeCachedForAnHourAndAnErrorCannot()
    {
        await using var host = await StartAsync([House('1', "2026-05-01", 1)]);
        await using var empty = await ApiHost.StartAsync(postgres, new FakeReleaseSite());

        using var ok = await host.Client.GetAsync("/v1/home");
        using var none = await empty.Client.GetAsync("/v1/home");

        Assert.Equal("public, max-age=3600", ok.Headers.CacheControl?.ToString());
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
        Assert.Equal(0, answer.GetProperty("votes").GetArrayLength());
    }
}
