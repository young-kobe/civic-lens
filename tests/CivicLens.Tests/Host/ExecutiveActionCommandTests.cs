using System.Text.Json;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ExecutiveActionCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "executive-action-command-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private ExecutiveActionSeeder executiveActions = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        executiveActions = new ExecutiveActionSeeder(connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task TheTargetCommandsRunTheTargetBackfillAndTheRevisionCommandsRunTheRevisionBackfillSoASwappedArgumentFailsHere()
    {
        await AdmitWithNotesAsync();
        await ExecuteAsync("DELETE FROM executive_action_revision_disposition_targets");

        var revisionsVerify = await RunAsync("executive-actions", "revisions", "verify");
        var targetsVerify = await RunAsync("executive-actions", "targets", "verify");
        var revisionsBackfill = await RunAsync("executive-actions", "revisions", "backfill", "5");
        var targetsBackfill = await RunAsync("executive-actions", "targets", "backfill", "5");

        Assert.Equal(0, revisionsVerify.ExitCode);
        Assert.Equal(1, targetsVerify.ExitCode);
        Assert.Contains("\"legacy\":1", targetsVerify.Output, StringComparison.Ordinal);
        Assert.Contains("\"moved\":0", revisionsBackfill.Output, StringComparison.Ordinal);
        Assert.Contains("\"moved\":1", targetsBackfill.Output, StringComparison.Ordinal);
        Assert.Equal(0, targetsBackfill.ExitCode);
    }

    [Fact]
    public async Task ALegacyRevisionIsSeenByTheRevisionCommandsAndNotByTheTargetCommands()
    {
        await AdmitWithNotesAsync();
        await executiveActions.MakeLegacyAsync();

        var targetsVerify = await RunAsync("executive-actions", "targets", "verify");
        var revisionsVerify = await RunAsync("executive-actions", "revisions", "verify");

        Assert.Equal(0, targetsVerify.ExitCode);
        Assert.Contains("\"checked\":0", targetsVerify.Output, StringComparison.Ordinal);
        Assert.Equal(1, revisionsVerify.ExitCode);
        Assert.Contains("\"legacy\":1", revisionsVerify.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("executive-actions")]
    [InlineData("executive-actions", "targets")]
    [InlineData("executive-actions", "targets", "backfill")]
    [InlineData("executive-actions", "targets", "verify", "now")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync(arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("executive-actions targets verify", result.Error, StringComparison.Ordinal);
    }

    private Task AdmitWithNotesAsync()
    {
        var notes = JsonSerializer.Serialize("Amends: EO 13224, September 23, 2001");
        var json = FederalRegisterFixtures.TrumpOrder.Replace("\"html_url\":", $"\"disposition_notes\":{notes},\"html_url\":", StringComparison.Ordinal);
        return executiveActions.AdmitAsync(json, "donald-trump");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner" }, arguments);
}
