using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicLens.Tests.PublicApi;

/// <summary>The search service on a loopback port over an empty database, reading a fake release site. The vote routes read a migrated operational database through the login of the public role.</summary>
internal sealed class ApiHost : IAsyncDisposable
{
    private readonly PostgresCollection postgres;
    private readonly WebApplication app;

    private const string PublicRole = "civic_lens_public";
    private const string PublicPassword = "test-only-password";

    private ApiHost(PostgresCollection postgres, WebApplication app, PublicApiOptions options, FakeReleaseSite site, string operationalDatabase)
    {
        OperationalDatabase = operationalDatabase;
        this.postgres = postgres;
        this.app = app;
        Options = options;
        Site = site;
        Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    public PublicApiOptions Options { get; }
    public FakeReleaseSite Site { get; }
    public HttpClient Client { get; }
    /// <summary>The operational database as its owner, for a test to seed the published records that the views show.</summary>
    public string OperationalDatabase { get; }

    public static async Task<ApiHost> StartAsync(PostgresCollection postgres, FakeReleaseSite site, Func<PublicApiOptions, PublicApiOptions>? change = null,
        HttpMessageHandler? turnstile = null, ILoggerProvider? logProvider = null)
    {
        var operational = await postgres.CreateMigratedDatabaseAsync();
        await ExecuteAsync(operational, $"ALTER ROLE {PublicRole} LOGIN PASSWORD '{PublicPassword}'");
        var options = new PublicApiOptions
        {
            ReleaseBaseUrl = PublicApiOptions.Directory("http://release.test"),
            Database = await postgres.CreateEmptyDatabaseAsync(),
            PublicViewsDatabase = new NpgsqlConnectionStringBuilder(operational) { Username = PublicRole, Password = PublicPassword }.ConnectionString
        };
        options = change?.Invoke(options) ?? options;
        var app = PublicApiApp.Build(options, logProvider is null ? ["--logging:LogLevel:Default=None"] : [], new FeedReleaseSource(new ReleaseFeed(new HttpClient(site) { BaseAddress = options.ReleaseBaseUrl })), turnstile, logProvider);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        return new ApiHost(postgres, app, options, site, operational);
    }

    /// <summary>Waits until the ingest worker made the given release the active one in the search database.</summary>
    public async Task WaitForReleaseAsync(int number = 1)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await ActiveSearchReleaseAsync() == number) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"The service did not ingest release {number}.");
    }

    private async Task<int?> ActiveSearchReleaseAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(Options.Database);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT release_number FROM active_release", connection);
            return await command.ExecuteScalarAsync() as int?;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
        await postgres.DropDatabaseAsync(Options.Database);
        await ExecuteAsync(OperationalDatabase, $"ALTER ROLE {PublicRole} NOLOGIN PASSWORD NULL");
        await postgres.DropDatabaseAsync(OperationalDatabase);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
