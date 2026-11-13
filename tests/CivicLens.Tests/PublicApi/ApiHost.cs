using CivicLens.PublicApi;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicLens.Tests.PublicApi;

/// <summary>
/// The search service on a loopback port over an empty search database. The ingest and the read routes read a migrated operational database through the login of the public role.
/// <see cref="Site"/> publishes releases into that database.
/// </summary>
internal sealed class ApiHost : IAsyncDisposable
{
    private readonly PostgresCollection postgres;
    private readonly WebApplication app;

    private const string PublicRole = "civic_lens_public";
    private const string PublicPassword = "test-only-password";

    private ApiHost(PostgresCollection postgres, WebApplication app, PublicApiOptions options, TypedReleaseSite site, string operationalDatabase)
    {
        OperationalDatabase = operationalDatabase;
        this.postgres = postgres;
        this.app = app;
        Options = options;
        Site = site;
        Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    public PublicApiOptions Options { get; }
    public TypedReleaseSite Site { get; }
    public HttpClient Client { get; }
    /// <summary>The operational database as its owner, for a test to seed the published records that the views show.</summary>
    public string OperationalDatabase { get; }

    /// <summary>Starts the service. <paramref name="publish"/> publishes releases before the start, so the first ingest tick finds them.</summary>
    public static async Task<ApiHost> StartAsync(PostgresCollection postgres, Func<PublicApiOptions, PublicApiOptions>? change = null,
        HttpMessageHandler? turnstile = null, ILoggerProvider? logProvider = null, Func<TypedReleaseSite, Task>? publish = null)
    {
        var operational = await postgres.CreateMigratedDatabaseAsync();
        var site = new TypedReleaseSite(operational);
        if (publish is not null) await publish(site);
        var options = new PublicApiOptions
        {
            Database = await postgres.CreateEmptyDatabaseAsync(),
            PublicViewsDatabase = await LogInPublicRoleAsync(operational)
        };
        options = change?.Invoke(options) ?? options;
        var app = PublicApiApp.Build(options, logProvider is null ? ["--logging:LogLevel:Default=None"] : [], turnstile, logProvider);
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
        await LogOutPublicRoleAsync(OperationalDatabase);
        await postgres.DropDatabaseAsync(OperationalDatabase);
    }

    /// <summary>Lets the role <c>civic_lens_public</c> log in and gives its connection to the operational database. The role belongs to the cluster, so each test logs it out again.</summary>
    public static async Task<string> LogInPublicRoleAsync(string operationalDatabase)
    {
        await ExecuteAsync(operationalDatabase, $"ALTER ROLE {PublicRole} LOGIN PASSWORD '{PublicPassword}'");
        return new NpgsqlConnectionStringBuilder(operationalDatabase) { Username = PublicRole, Password = PublicPassword }.ConnectionString;
    }

    public static Task LogOutPublicRoleAsync(string operationalDatabase) => ExecuteAsync(operationalDatabase, $"ALTER ROLE {PublicRole} NOLOGIN PASSWORD NULL");

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
