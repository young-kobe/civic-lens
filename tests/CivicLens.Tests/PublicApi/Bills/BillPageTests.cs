using System.Net;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Pages;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Bills;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BillPageTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly DateTimeOffset PublishedAt = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly string BillId = new('a', 64);
    private static readonly SeededBill Bill = new('a', "2026-03-02")
    {
        BillNumber = 7,
        Issues = ["health"],
        Sponsorships =
        [
            new("p1", "Sponsor", "2026-03-02"), new("p2", "Cosponsor", "2026-03-02"), new("p3", "Cosponsor", "2026-04-01", null, false),
            new("p4", "Cosponsor", "2026-04-02", "2026-04-20", false)
        ]
    };

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with { PublicBaseUrl = BaseUrl });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, Bill);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, Official("p1", "Pat One", "Democrat"), Official("p2", "Ann Two", "Democrat"),
            Official("p3", "Bob Three", "Republican"), Official("p4", "Cy Four", "Republican"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, """
            INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Care.');
            """);
        await SeedHistoryAsync(host.OperationalDatabase);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('e', "2026-03-03") { BillNumber = 7 }, new SeededVote('f', "2026-03-04") { BillNumber = 8 });
        return host;
    }

    private static SeededOfficial Official(string id, string name, string party) =>
        new(id, name, new SeededTerm("House", "Representative", "2020-01-01") { Party = party, State = "MA", District = 1 });

    [Fact]
    public async Task TheServerPageShowsTheBillFromOnePublishedReleaseWithItsStepsBackersAndVotes()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync($"{ServerPageEndpoints.Prefix}/records/{BillId}.html");
        var page = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("\"r1-", response.Headers.ETag!.Tag);
        Assert.StartsWith("H.R. 7, ", page.QuerySelector("main h1")!.TextContent);
        Assert.Equal($"https://civic-lens.test/records/{BillId}.html", page.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal(["Introduced in the House", "Passed the House", "Became law"], page.QuerySelectorAll(".bill-stepper b").Select(step => step.TextContent));
        Assert.Equal("2 Mar 2026, by Pat One", page.QuerySelector(".bill-stepper li")!.QuerySelector(".bill-step-detail")!.TextContent);
        Assert.Contains("Sponsored by Pat One, a Democrat.", page.QuerySelector(".bill-head .bill-lede")!.TextContent);
        Assert.Contains("This site tracks 2 House members who cosponsored it: 1 on the day it was introduced and 1 who joined later.", page.QuerySelector("#bill-backers-title")!.ParentElement!.TextContent);
        Assert.DoesNotContain("Cy Four", page.QuerySelector("#bill-backers-title")!.ParentElement!.TextContent);
        Assert.Equal(["1 Democrats", "1 Republicans"], page.QuerySelectorAll(".bill-party li").Select(item => item.TextContent.Trim()));
        Assert.Equal(["Health care"], page.QuerySelectorAll(".bill-chip").Select(chip => chip.TextContent));
        var votes = page.QuerySelectorAll(".bill-vote a").ToArray();
        Assert.Equal([$"https://civic-lens.test/records/{new string('e', 64)}.html"], votes.Select(link => link.GetAttribute("href")));
    }

    [Fact]
    public async Task AVoteRecordAndAnUnknownRecordGetTheNotFoundPage()
    {
        await using var host = await StartAsync();

        foreach (var id in new[] { new string('e', 64), new string('9', 64), "not-an-id" })
        {
            var response = await host.Client.GetAsync($"{ServerPageEndpoints.Prefix}/records/{id}.html");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ThePageAnswers304ForTheETagOfItsRelease()
    {
        await using var host = await StartAsync();
        var path = $"{ServerPageEndpoints.Prefix}/records/{BillId}.html";
        var first = await host.Client.GetAsync(path);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var second = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    private static async Task SeedHistoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SET session_replication_role = replica;
            INSERT INTO published_bill_histories (detail_job_id, source_id, congress, bill_type, bill_number, observed_at_utc_ticks, action_count, public_law)
            VALUES ('history', 'congress', 119, 'hr', 7, 1, 3, '119-7');
            INSERT INTO published_bill_history_actions (detail_job_id, position, action_date, text, type) VALUES
              ('history', 0, DATE '2026-03-05', 'Passed/agreed to in House: On passage Passed by voice vote.', 'Floor'),
              ('history', 1, DATE '2026-03-06', 'Presented to President.', 'President'),
              ('history', 2, DATE '2026-03-07', 'Became Public Law No: 119-7.', 'BecameLaw');
            INSERT INTO release_bill_histories (release_number, record_id, detail_job_id) VALUES (1, @record, 'history');
            RESET session_replication_role;
            """, connection);
        command.Parameters.AddWithValue("record", BillId);
        await command.ExecuteNonQueryAsync();
    }
}
