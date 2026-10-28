using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Configuration;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Roster;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RosterCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string configurationPath = Path.Combine(Path.GetTempPath(), "roster-config-" + Guid.NewGuid().ToString("N") + ".json");
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public async Task DisposeAsync()
    {
        File.Delete(configurationPath);
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Fact]
    public async Task ProposeListsNewMembersWithAManualPressStepAndConfiguredMembersWhoLeftAndWritesNothing()
    {
        await SeedSnapshotAsync();
        WriteConfiguration(
            RosterFixtures.Person("maria-cantwell", "Maria Cantwell", "C000127"),
            RosterFixtures.Person("retired-member", "Retired Member", "R000001"),
            RosterFixtures.Person("no-bioguide", "No Bioguide", null));

        var result = await HostProcess.RunAsync(connectionString, "roster", "propose", configurationPath);

        Assert.Equal(0, result.ExitCode);
        using var output = JsonDocument.Parse(result.Output);
        var joins = output.RootElement.GetProperty("joins").EnumerateArray().ToArray();
        Assert.Equal(["N000147", "W000832"], joins.Select(join => join.GetProperty("bioguideId").GetString()));
        Assert.Equal(["eleanor-holmes-norton", "aisha-wahab"], joins.Select(join => join.GetProperty("id").GetString()));
        Assert.All(joins, join => Assert.Contains("press sources", join.GetProperty("note").GetString(), StringComparison.OrdinalIgnoreCase));
        var leaves = output.RootElement.GetProperty("leaves").EnumerateArray().ToArray();
        Assert.Equal("retired-member", Assert.Single(leaves).GetProperty("id").GetString());
    }

    [Fact]
    public async Task ProposeWithoutASnapshotFailsInsteadOfProposingEveryoneAsLeaving()
    {
        WriteConfiguration(RosterFixtures.Person("retired-member", "Retired Member", "R000001"));

        var result = await HostProcess.RunAsync(connectionString, "roster", "propose", configurationPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No roster snapshot", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposeNeedsAConfigurationWithARosterSource()
    {
        var configuration = RosterFixtures.Configuration() with { Sources = [] };
        File.WriteAllText(configurationPath, System.Text.Json.JsonSerializer.Serialize(configuration, ConfigurationJson.Options));

        var result = await HostProcess.RunAsync(connectionString, "roster", "propose", configurationPath);

        Assert.Equal(2, result.ExitCode);
    }

    [Theory]
    [InlineData("roster")]
    [InlineData("roster", "propose")]
    [InlineData("roster", "apply", "x")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await HostProcess.RunAsync(connectionString, arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use roster propose", result.Error, StringComparison.Ordinal);
    }

    private async Task SeedSnapshotAsync()
    {
        var attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var attemptId = await RosterFixtures.ImportCaptureAsync(attempts, new string('a', 64), DateTimeOffset.UtcNow);
        await PostgresMemberRosterStore.FromConnectionString(connectionString).AdmitAsync(attemptId, RosterFixtures.Members(), null, default);
    }

    private void WriteConfiguration(params PersonConfiguration[] people) =>
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(RosterFixtures.Configuration(people), ConfigurationJson.Options));
}
