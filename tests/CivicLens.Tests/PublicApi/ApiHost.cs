using System.Net;
using System.Net.Http.Json;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Search;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace CivicLens.Tests.PublicApi;

/// <summary>The search service on a loopback port over an empty database, reading a fake release site.</summary>
internal sealed class ApiHost : IAsyncDisposable
{
    private readonly PostgresCollection postgres;
    private readonly WebApplication app;

    private ApiHost(PostgresCollection postgres, WebApplication app, PublicApiOptions options, FakeReleaseSite site)
    {
        this.postgres = postgres;
        this.app = app;
        Options = options;
        Site = site;
        Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    public PublicApiOptions Options { get; }
    public FakeReleaseSite Site { get; }
    public HttpClient Client { get; }

    public static async Task<ApiHost> StartAsync(PostgresCollection postgres, FakeReleaseSite site, Func<PublicApiOptions, PublicApiOptions>? change = null,
        HttpMessageHandler? turnstile = null, ILoggerProvider? logProvider = null)
    {
        var options = new PublicApiOptions
        {
            ReleaseBaseUrl = PublicApiOptions.Directory("http://release.test"),
            Database = await postgres.CreateEmptyDatabaseAsync()
        };
        options = change?.Invoke(options) ?? options;
        var app = PublicApiApp.Build(options, logProvider is null ? ["--logging:LogLevel:Default=None"] : [], site, turnstile, logProvider);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        return new ApiHost(postgres, app, options, site);
    }

    /// <summary>Waits until the ingest worker made the given release the active one.</summary>
    public async Task WaitForReleaseAsync(int number = 1)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var response = await Client.GetAsync("/v1/release");
            if (response.StatusCode == HttpStatusCode.OK &&
                (await response.Content.ReadFromJsonAsync<ReleaseInfo>(PublicationProtocol.JsonOptions))!.ReleaseNumber == number) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"The service did not ingest release {number}.");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
        await postgres.DropDatabaseAsync(Options.Database);
    }
}
