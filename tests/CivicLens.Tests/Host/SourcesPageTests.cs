using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Health;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Review;
using CivicLens.Host.Collection;
using CivicLens.Host.Components.Ui;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Processing;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class SourcesPageTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string HostileOfficial = "Ada <img src=x onerror=alert(1)> Official";
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly DateTimeOffset Retry = new(2026, 10, 9, 8, 5, 0, TimeSpan.Zero);
    private ReviewWorkspaceHost? host;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (host is not null) await host.StopAsync();
    }

    // The page is interactive, so its first response must already hold the real sources and their health.
    [Fact]
    public async Task PrerenderedPageShowsConfiguredSourcesAndTheirHealth()
    {
        var workspace = await StartAsync("auth0|owner");
        var before = await Host.GetDocumentAsync("/Review/Sources");
        var row = before.QuerySelector("table tbody tr")!;
        Assert.Contains("example.test/pages/a", row.TextContent);
        Assert.Equal("Not checked yet", row.QuerySelector(".badge")!.TextContent);
        Assert.Contains("1 configured", before.QuerySelector(".panel-aside")!.TextContent);

        await workspace.EnqueueAsync(Owner, "source", Guid.NewGuid().ToString("N"), default);
        using var response = await Host.Client.GetAsync("/Review/Sources");
        var html = await response.Content.ReadAsStringAsync();
        var after = await new HtmlParser().ParseDocumentAsync(html);

        Assert.Contains("<!--Blazor:", html, StringComparison.Ordinal);
        var badges = after.QuerySelectorAll(".badge-live");
        Assert.Equal(2, badges.Length);
        Assert.All(badges, badge => Assert.Equal("Checking", badge.TextContent));
        var checkNow = after.QuerySelectorAll("button").Single(button => button.TextContent == "Check now");
        Assert.True(checkNow.HasAttribute("disabled"));
        Assert.Contains(after.QuerySelectorAll("button"), button => button.TextContent == "Stop");
    }

    // Only the owner may run or stop collection; reviewers are sent to the access-denied page before any data renders.
    [Fact]
    public async Task ReviewersAreDeniedTheSourcesPage()
    {
        await StartAsync("auth0|friend");

        using var denied = await Host.Client.GetAsync("/Review/Sources");

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("access-denied", denied.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Ada", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // Configuration is operator input, but names still reach a browser and must never become markup.
    [Fact]
    public async Task ConfiguredTextIsEscaped()
    {
        await StartAsync("auth0|owner");

        using var response = await Host.Client.GetAsync("/Review/Sources");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
    }

    // Live updates travel over the Blazor connection; a policy that blocks it would freeze the page silently.
    [Fact]
    public async Task SecurityPolicyAllowsTheInteractiveConnection()
    {
        await StartAsync("auth0|owner");

        using var page = await Host.Client.GetAsync("/Review/Sources");
        var policy = string.Join("; ", page.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("connect-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        using var script = await Host.Client.GetAsync("/_framework/blazor.web.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        using var negotiate = await Host.Client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
    }

    // An open Sources page must lose its live connection when the sign-in expires, so no later read or action runs on a stale sign-in.
    [Fact]
    public async Task InteractiveConnectionClosesWhenTheSignInExpires()
    {
        await StartAsync("auth0|owner");
        var cookie = Host.CreateCookie("auth0|owner", TimeSpan.FromSeconds(4));
        Host.Client.DefaultRequestHeaders.Remove("Cookie");
        Host.Client.DefaultRequestHeaders.Add("Cookie", cookie);
        using var negotiate = await Host.Client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);
        using var body = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync());
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Cookie", cookie);
        socket.Options.SetRequestHeader("Host", "review.example.test");
        var uri = new UriBuilder(Host.Client.BaseAddress!)
        {
            Scheme = "ws",
            Path = "/_blazor",
            Query = "id=" + body.RootElement.GetProperty("connectionToken").GetString()
        }.Uri;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await socket.ConnectAsync(uri, deadline.Token);
        await socket.SendAsync("{\"protocol\":\"blazorpack\",\"version\":1}\u001e"u8.ToArray(), WebSocketMessageType.Text, true, deadline.Token);
        var buffer = new byte[4096];
        var handshake = await socket.ReceiveAsync(buffer, deadline.Token);
        Assert.Equal("{}\u001e", Encoding.UTF8.GetString(buffer, 0, handshake.Count));

        try
        {
            while ((await socket.ReceiveAsync(buffer, deadline.Token)).MessageType != WebSocketMessageType.Close) { }
        }
        catch (WebSocketException) { }
    }

    // A repeated click or a retry after an unknown outcome must not queue a second check.
    [Fact]
    public async Task QueueingWithTheSameRequestKeyCreatesOneCheck()
    {
        var workspace = await StartAsync("auth0|owner");
        var key = Guid.NewGuid().ToString("N");

        var first = await workspace.EnqueueAsync(Owner, "source", key, default);
        var repeated = await workspace.EnqueueAsync(Owner, "source", key, default);

        Assert.Equal(first.JobId, repeated.JobId);
        var job = Assert.Single(await Jobs.ListAsync(50, default));
        Assert.Empty(job.Attempts);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.EnqueueAsync(
            new ReviewActor("auth0|friend", ReviewRole.Reviewer), "source", Guid.NewGuid().ToString("N"), default));
        Assert.Single(await Jobs.ListAsync(50, default));
    }

    // Stop must end a waiting check and the page must then report it as stopped, not as a failure.
    [Fact]
    public async Task StopCancelsAQueuedCheck()
    {
        var workspace = await StartAsync("auth0|owner");
        var job = await workspace.EnqueueAsync(Owner, "source", Guid.NewGuid().ToString("N"), default);

        var stopped = await workspace.CancelAsync(Owner, job.JobId, default);

        Assert.Equal(CollectionJobState.Cancelled, stopped!.State);
        var sources = await workspace.GetSourcesAsync(Owner, null, null);
        Assert.Equal(SourceCheckState.Cancelled, sources.ChecksByJobId[job.JobId].State);
        var page = await Host.GetDocumentAsync("/Review/Sources");
        Assert.Contains(page.QuerySelectorAll(".badge"), badge => badge.TextContent == "Stopped");
    }

    // Evidence imported after a lost receipt is still evidence, and Check again must reuse only that saved page URL.
    [Fact]
    public async Task CheckAgainRecollectsTheSavedPageOnceAndKeptEvidenceIsShown()
    {
        var workspace = await StartAsync("auth0|owner");
        var job = await workspace.EnqueueAsync(Owner, "source", Guid.NewGuid().ToString("N"), default);
        const string attemptId = "aabbccddeeff00112233445566778899";
        await SettleWithoutReceiptAsync(job, attemptId);

        var details = await workspace.GetSourcesAsync(Owner, job.JobId, null);
        var timeline = CheckTimeline.Build(details.SelectedJob!, details.RetainedAttempts);
        Assert.StartsWith("Saved evidence is kept, but the transfer receipt is missing.", Assert.Single(timeline).Text, StringComparison.Ordinal);

        var key = Guid.NewGuid().ToString("N");
        var again = await workspace.RecollectAsync(Owner, job.JobId, key, default);
        var replay = await workspace.RecollectAsync(Owner, job.JobId, key, default);

        Assert.Equal(again.JobId, replay.JobId);
        Assert.NotEqual(job.JobId, again.JobId);
        Assert.Equal(CollectionMode.Page, again.Definition.Mode);
        Assert.Equal(job.Definition.Url, again.Definition.Url);
        Assert.Equal(2, (await Jobs.ListAsync(50, default)).Count);
    }

    [Theory]
    [InlineData(SourceCheckState.NeverChecked, "Not checked yet", Tone.Neutral)]
    [InlineData(SourceCheckState.Checking, "Checking", Tone.Live)]
    [InlineData(SourceCheckState.Failed, "Failed", Tone.Bad)]
    [InlineData(SourceCheckState.Cancelled, "Stopped", Tone.Neutral)]
    [InlineData(SourceCheckState.ChangeFound, "Change found", Tone.Ok)]
    [InlineData(SourceCheckState.BaselineSaved, "First version saved", Tone.Neutral)]
    [InlineData(SourceCheckState.UpToDate, "Up to date", Tone.Ok)]
    public void EachCheckStateHasOneLabelAndTone(SourceCheckState state, string label, Tone tone)
    {
        Assert.Equal(new SourceStatus(label, tone), SourceStatusDisplay.Status(Health(state)));
    }

    // An automatic retry needs no action, so it must not look like a failure that needs the owner.
    [Fact]
    public void FailureWithAScheduledRetryIsAWarningThatNamesTheRetryTime()
    {
        var status = SourceStatusDisplay.Status(Health(SourceCheckState.Failed) with { RetryAt = Retry });

        Assert.Equal(new SourceStatus("Failed, retrying 9 Oct, 08:05", Tone.Warn), status);
    }

    // A failed check is missing evidence; readers must not take it as proof that nothing changed.
    [Fact]
    public void FailuresAreDescribedAsCoverageGaps()
    {
        Assert.Contains("coverage gap, not evidence of no activity", SourceStatusDisplay.Detail(Health(SourceCheckState.Failed)), StringComparison.Ordinal);
        var request = Fixtures.CollectionFixtures.Request();
        var failed = Fixtures.CollectionFixtures.Receipt(request) with
        {
            Outcome = CollectionOutcome.Failed,
            Capture = null,
            FailureCode = CollectionFailureCode.HttpError
        };
        var job = new CollectionJobRecord(new string('1', 32), CollectionJobDefinition.FromConfiguration(new CollectionConfiguration
        {
            Version = 2,
            People = [new PersonConfiguration { Id = "person", Name = "Official" }],
            Sources = [new WatchedSourceConfiguration { Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }], Url = request.Url, AllowedOrigin = request.AllowedOrigin, AllowedPathPrefix = request.AllowedPathPrefix }]
        }, "source"), "key", CollectionJobState.Failed, Retry, null, false, 0, 0, 0,
            [new CollectionJobAttempt("attempt", request, new CollectionAttemptResolution(CollectionJobAttemptOutcome.PermanentFailure, failed), Retry, Retry)]);

        var item = Assert.Single(CheckTimeline.Build(job, new Dictionary<string, StoredCollectionAttempt>()));

        Assert.Equal(Tone.Bad, item.Tone);
        Assert.Equal("Check failed: the site returned an error. This is a coverage gap, not evidence of no activity.", item.Text);
    }

    private ReviewWorkspaceHost Host => host!;

    private PostgresCollectionJobStore Jobs => PostgresCollectionJobStore.FromConnectionString(Host.ConnectionString);

    private async Task<CollectionWorkspace> StartAsync(string subject)
    {
        host = new ReviewWorkspaceHost(postgres) { OfficialName = HostileOfficial };
        await host.StartAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie(subject));
        var configuration = JsonSerializer.Deserialize<CollectionConfiguration>(
            await File.ReadAllTextAsync(Path.Combine(host.KeyDirectory, "sources.json")), CollectionProtocol.JsonOptions)!;
        return new CollectionWorkspace(configuration, Jobs, host.Attempts,
            PostgresEvidenceProcessingStore.FromConnectionString(host.ConnectionString));
    }

    private async Task SettleWithoutReceiptAsync(CollectionJobRecord job, string attemptId)
    {
        var request = job.Definition.CreateRequest(attemptId, Host.KeyDirectory);
        await Host.SaveImportedAttemptAsync(request, attemptId, "Retained article text.\n");
        var resolution = JsonSerializer.Serialize(new CollectionAttemptResolution(
            CollectionJobAttemptOutcome.Succeeded, null, "EvidenceAlreadyImported"), CollectionProtocol.JsonOptions);
        await using var connection = new NpgsqlConnection(Host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at, resolution_json, completed_at)
            VALUES (@attempt, @job, 1, @request, @started, @resolution, @completed);
            UPDATE collection_jobs SET state = 'Succeeded' WHERE job_id = @job;
            """, connection);
        command.Parameters.AddWithValue("attempt", attemptId);
        command.Parameters.AddWithValue("job", job.JobId);
        command.Parameters.AddWithValue("request", JsonSerializer.Serialize(request, CollectionProtocol.JsonOptions));
        command.Parameters.AddWithValue("started", DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.Ticks);
        command.Parameters.AddWithValue("resolution", resolution);
        command.Parameters.AddWithValue("completed", DateTimeOffset.UtcNow.UtcDateTime.Ticks);
        await command.ExecuteNonQueryAsync();
    }

    private static SourceHealth Health(SourceCheckState state) => new("source", state, null, null, null, false);
}
