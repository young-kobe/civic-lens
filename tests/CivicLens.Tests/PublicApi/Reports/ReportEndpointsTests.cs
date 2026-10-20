using System.Net;
using System.Text.Json;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Reports;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Reports;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReportEndpointsTests(PostgresCollection postgres)
{
    private static readonly string RecordId = new('a', 64);
    private const string PullToken = "0123456789abcdef0123456789abcdef";

    private async Task<ApiHost> StartAsync(FakeTurnstile turnstile, Func<PublicApiOptions, PublicApiOptions>? change = null, FakeReleaseSite? site = null)
    {
        site ??= new FakeReleaseSite();
        if (site.Requests.Count == 0) site.Publish(1, PublishedSamples.Statement('a', "Rent cap"));
        var host = await ApiHost.StartAsync(postgres, site, options =>
        {
            options = options with { Turnstile = new("secret", FakeTurnstile.Hostname), ReportPullToken = PullToken };
            return change?.Invoke(options) ?? options;
        }, turnstile);
        await host.WaitForReleaseAsync();
        return host;
    }

    private static Task<HttpResponseMessage> PostAsync(ApiHost host, string category = "wrong-quote", string text = "", string recordId = "", string revision = "1") =>
        host.Client.PostAsync(ReaderReportProtocol.SubmitPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [ReaderReportProtocol.RecordIdField] = recordId.Length > 0 ? recordId : RecordId,
            [ReaderReportProtocol.RevisionField] = revision,
            [ReaderReportProtocol.CategoryField] = category,
            [ReaderReportProtocol.TextField] = text,
            [ReaderReportProtocol.TurnstileField] = "token-1"
        }));

    private static async Task<long> CountAsync(ApiHost host)
    {
        await using var database = NpgsqlDataSource.Create(host.Options.Database);
        await using var command = database.CreateCommand("SELECT count(*) FROM reader_reports");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task AValidReportIsStoredOnceWithTheKindAndTheActiveReleaseAndNothingAboutTheReader()
    {
        await using var host = await StartAsync(new FakeTurnstile());

        var response = await PostAsync(host, "other", "The date is wrong.");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        await using var database = NpgsqlDataSource.Create(host.Options.Database);
        await using var command = database.CreateCommand("SELECT kind, release_number, category, body FROM reader_reports");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(("statement", 1, "other", "The date is wrong."), (reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        Assert.False(await reader.ReadAsync());
        Assert.Equal(["sequence", "report_id", "record_id", "revision_number", "kind", "release_number", "category", "body", "payload_hash", "received_at"],
            await ColumnsAsync(host));
    }

    [Fact]
    public async Task ADuplicateReportGetsTheSameAnswerAsTheFirstAndIsNotStoredTwiceSoARetryCostsNothing()
    {
        await using var host = await StartAsync(new FakeTurnstile());

        var first = await PostAsync(host, "wrong-date");
        var second = await PostAsync(host, "wrong-date");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(1, await CountAsync(host));
    }

    [Theory]
    [InlineData("wrong-quote", "", "b", "1", HttpStatusCode.NotFound)]
    [InlineData("wrong-quote", "", "a", "2", HttpStatusCode.NotFound)]
    [InlineData("rude", "", "a", "1", HttpStatusCode.BadRequest)]
    [InlineData("other", "", "a", "1", HttpStatusCode.BadRequest)]
    [InlineData("other", "   ", "a", "1", HttpStatusCode.BadRequest)]
    [InlineData("wrong-quote", "bad\0text", "a", "1", HttpStatusCode.BadRequest)]
    [InlineData("wrong-quote", "TOO LONG", "a", "1", HttpStatusCode.BadRequest)]
    [InlineData("wrong-quote", "", "a", "0", HttpStatusCode.BadRequest)]
    public async Task AnInvalidReportIsRefusedAndNoRowIsWrittenBecauseTheDatabaseHoldsOnlyReportsAboutRealRecordRevisions(
        string category, string text, string idCharacter, string revision, HttpStatusCode expected)
    {
        await using var host = await StartAsync(new FakeTurnstile());
        if (text == "TOO LONG") text = new string('x', ReaderReportProtocol.MaximumTextCharacters + 1);

        var response = await PostAsync(host, category, text, new string(idCharacter[0], 64), revision);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, await CountAsync(host));
    }

    [Fact]
    public async Task TextOfExactlyTheMaximumLengthIsAccepted()
    {
        await using var host = await StartAsync(new FakeTurnstile());

        var response = await PostAsync(host, "other", new string('x', ReaderReportProtocol.MaximumTextCharacters));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task AReportOnAReplacedRevisionIsAcceptedBecauseTheReaderMayHoldAnOldPage()
    {
        var site = new FakeReleaseSite();
        site.Publish(1, PublishedSamples.Statement('a', "Rent cap"));
        await using var host = await StartAsync(new FakeTurnstile(), options => options with { PollInterval = TimeSpan.FromMilliseconds(100) }, site);
        site.Publish(2, PublishedSamples.Statement('a', "Rent cap", revision: 2));
        await host.WaitForReleaseAsync(2);

        var response = await PostAsync(host, revision: "1");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var database = NpgsqlDataSource.Create(host.Options.Database);
        await using var command = database.CreateCommand("SELECT release_number FROM reader_reports");
        Assert.Equal(2, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task AFailedBotCheckIsRefusedWithNoRowBecauseTheCheckStandsBeforeTheWrite()
    {
        await using var host = await StartAsync(new FakeTurnstile { Success = false });

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(host)).StatusCode);
        Assert.Equal(0, await CountAsync(host));
    }

    [Fact]
    public async Task AReplyForAnotherHostnameIsRefusedBecauseAPassedTokenOfAnotherSiteProvesNothingHere()
    {
        await using var host = await StartAsync(new FakeTurnstile { ReplyHostname = "other.example" });

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(host)).StatusCode);
        Assert.Equal(0, await CountAsync(host));
    }

    [Fact]
    public async Task ABotCheckTimeoutFailsClosedWith503AndNoRow()
    {
        await using var host = await StartAsync(new FakeTurnstile { Failure = new TaskCanceledException("The request timed out.") });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await PostAsync(host)).StatusCode);
        Assert.Equal(0, await CountAsync(host));
    }

    [Fact]
    public async Task WithoutATurnstileSecretTheReportRouteAnswers503AndStoresNothing()
    {
        await using var host = await StartAsync(new FakeTurnstile(), options => options with { Turnstile = null });

        var response = await PostAsync(host);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("reports are not available", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountAsync(host));
    }

    [Fact]
    public async Task OnlyTheSecretAndTheTokenReachTheBotCheckBecauseTheReportTextIsNotCloudflaresBusiness()
    {
        var turnstile = new FakeTurnstile();
        await using var host = await StartAsync(turnstile);

        await PostAsync(host, "other", "Private words about the record.");

        var body = Assert.Single(turnstile.Bodies);
        Assert.Equal("secret=secret&response=token-1", body);
    }

    [Fact]
    public async Task EachClientMayReportOnlyTheConfiguredCountEachHourAndTheNextReportGets429()
    {
        await using var host = await StartAsync(new FakeTurnstile(), options => options with { ReportsPerHour = 2 });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++) statuses.Add((await PostAsync(host, text: $"report {i}", category: "other")).StatusCode);

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task AtTheDailyCapReportsAreClosedWith503AndNothingMoreIsStored()
    {
        await using var host = await StartAsync(new FakeTurnstile(), options => options with { ReportsPerDay = 2 });

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage last = null!;
        for (var i = 0; i < 3; i++)
        {
            last = await PostAsync(host, text: $"report {i}", category: "other");
            statuses.Add(last.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.ServiceUnavailable], statuses);
        Assert.Contains("reports are closed for today", await last.Content.ReadAsStringAsync());
        Assert.Equal(2, await CountAsync(host));
    }

    [Fact]
    public async Task ABodyOverTheRouteCapIsRefusedBeforeAnyCheckBecauseTheRouteIsOpenToAnyone()
    {
        var turnstile = new FakeTurnstile();
        await using var host = await StartAsync(turnstile);

        var response = await PostAsync(host, "other", new string('x', ReaderReportProtocol.MaximumRequestBytes * 2));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(turnstile.Bodies);
    }

    [Fact]
    public async Task AJsonBodyIsRefusedBecauseOnlyTheFormEncodingNeedsNoPreflight()
    {
        await using var host = await StartAsync(new FakeTurnstile());

        var response = await host.Client.PostAsync(ReaderReportProtocol.SubmitPath, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-token-wrong-token-wrong-token")]
    [InlineData("Basic 0123456789abcdef0123456789abcdef")]
    public async Task ThePullRefusesAMissingOrWrongTokenWith401(string? header)
    {
        await using var host = await StartAsync(new FakeTurnstile());
        await PostAsync(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, ReaderReportProtocol.PullPath);
        if (header is not null) request.Headers.TryAddWithoutValidation("Authorization", header);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("wrong-quote", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ThePullReturnsTheReportsWithTheRightTokenAndTheyPassTheContractValidation()
    {
        await using var host = await StartAsync(new FakeTurnstile());
        await PostAsync(host, "wrong-date");
        using var request = new HttpRequestMessage(HttpMethod.Get, ReaderReportProtocol.PullPath);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + PullToken);

        var response = await host.Client.SendAsync(request);

        var batch = JsonSerializer.Deserialize<PulledReportBatch>(await response.Content.ReadAsStringAsync(), PublicationProtocol.JsonOptions)!;
        batch.Validate();
        Assert.Equal(("wrong-date", PublishedRecordKind.Statement), (batch.Reports.Single().Category, batch.Reports.Single().Kind));
    }

    [Fact]
    public async Task WithoutAPullTokenThePullRouteDoesNotExist()
    {
        await using var host = await StartAsync(new FakeTurnstile(), options => options with { ReportPullToken = null });

        var response = await host.Client.GetAsync(ReaderReportProtocol.PullPath);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<string[]> ColumnsAsync(ApiHost host)
    {
        await using var database = NpgsqlDataSource.Create(host.Options.Database);
        await using var command = database.CreateCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'reader_reports' ORDER BY ordinal_position");
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        return [.. columns];
    }
}
