using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class AnalysisCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "analyses_cli_" + Guid.NewGuid().ToString("N");
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();
    }

    public Task DisposeAsync() => ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");

    [Fact]
    public async Task TheOwnerRequeuesAllOrOneFailedEntry()
    {
        var all = await RunAsync("auth0|owner", "analyses", "requeue", "--all");
        var one = await RunAsync("auth0|owner", "analyses", "requeue", new string('c', 64));

        Assert.Equal((0, """{"requeued":0}"""), (all.ExitCode, all.Output.Trim()));
        Assert.Equal((0, """{"requeued":0}"""), (one.ExitCode, one.Output.Trim()));
    }

    [Theory]
    [InlineData("analyses")]
    [InlineData("analyses", "requeue")]
    [InlineData("analyses", "requeue", "a", "b")]
    [InlineData("analyses", "retry", "--all")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use analyses requeue", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidComparisonIdIsRejected()
    {
        var result = await RunAsync("auth0|owner", "analyses", "requeue", "not-a-hash");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("SHA-256", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAiDrafterCannotActAsTheOwner()
    {
        var result = await RunAsync("civic-lens:analysis", "analyses", "requeue", "--all");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("not the owner", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string owner, params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = owner }, arguments);

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var open = new NpgsqlConnection(connection);
        await open.OpenAsync();
        await using var command = new NpgsqlCommand(sql, open);
        await command.ExecuteNonQueryAsync();
    }
}
