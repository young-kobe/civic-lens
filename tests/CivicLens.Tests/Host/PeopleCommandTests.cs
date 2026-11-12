using System.Text.Json;
using CivicLens.Application.Configuration;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Roster;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PeopleCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string configurationPath = Path.Combine(Path.GetTempPath(), "people-config-" + Guid.NewGuid().ToString("N") + ".json");
    private static readonly Dictionary<string, string> Owner = new() { ["CIVIC_LENS_REVIEW_OWNER"] = "owner-subject" };
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public async Task DisposeAsync()
    {
        File.Delete(configurationPath);
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Theory]
    [InlineData("people")]
    [InlineData("people", "import", "config.json")]
    [InlineData("people", "export", "x")]
    [InlineData("people", "photo", "withhold")]
    [InlineData("people", "photo", "withhold", "--note")]
    [InlineData("people", "photo", "restore", "--note")]
    [InlineData("people", "photo", "hide", "mayor")]
    [InlineData("people", "photo", "restore", "mayor", "extra")]
    [InlineData("people", "website", "set", "mayor")]
    [InlineData("people", "website", "clear", "mayor", "https://example.gov/")]
    [InlineData("people", "website", "clear", "mayor", "--note")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await HostProcess.RunWithEnvironmentAsync(connectionString, null, Owner, arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use people photo withhold|restore", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithholdRestoreAndWebsiteEachAddOneRevisionWithTheOwnerAndTheNote()
    {
        await SeedPresidentAsync();

        var withhold = await RunAsync("people", "photo", "withhold", "donald-trump", "--note", "Takedown request 7.");
        var website = await RunAsync("people", "website", "set", "donald-trump", "https://www.whitehouse.gov/");
        var restore = await RunAsync("people", "photo", "restore", "donald-trump");
        var clear = await RunAsync("people", "website", "clear", "donald-trump");

        Assert.All(new[] { withhold, website, restore, clear }, result => Assert.Equal(0, result.ExitCode));
        Assert.Equal(1, JsonDocument.Parse(withhold.Output).RootElement.GetProperty("revisionNumber").GetInt32());
        Assert.Equal([(1, true, null, "Takedown request 7."), (2, true, "https://www.whitehouse.gov/", "Website set by the owner."),
                (3, false, "https://www.whitehouse.gov/", "Photo restored by the owner."), (4, false, null, "Website cleared by the owner.")],
            await ReadSettingsAsync());
    }

    [Fact]
    public async Task AWebsiteForAMemberWithAValidRosterUrlIsSavedAndWarnsThatTheRosterUrlPublishes()
    {
        await SeedRosterAsync(member => member);

        var result = await SetWebsiteAsync("maria-cantwell");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Warning: maria-cantwell has a roster website, https://www.cantwell.senate.gov. The roster website publishes, not this one.",
            result.Error.Trim());
        Assert.Equal(1, JsonDocument.Parse(result.Output).RootElement.GetProperty("revisionNumber").GetInt32());
        Assert.Equal([(1, false, "https://example.gov/", "Website set by the owner.")], await ReadSettingsAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://www.cantwell.senate.gov")]
    public async Task AWebsiteForAMemberWithNoValidRosterUrlIsSavedWithNoWarning(string? rosterUrl)
    {
        await SeedRosterAsync(member => member with { Url = rosterUrl });

        var result = await SetWebsiteAsync("maria-cantwell");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal([(1, false, "https://example.gov/", "Website set by the owner.")], await ReadSettingsAsync());
    }

    [Fact]
    public async Task AWebsiteForAPersonWhoIsNotAMemberIsSavedWithNoWarning()
    {
        await SeedRosterAsync(member => member);

        var result = await SetWebsiteAsync("no-bioguide");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Single(await ReadSettingsAsync());
    }

    [Fact]
    public async Task AWebsiteForAFormerMemberWhoIsNotInTheLatestRosterIsSavedWithNoWarning()
    {
        await SeedRosterAsync(member => member);

        var result = await SetWebsiteAsync("former-member");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Single(await ReadSettingsAsync());
    }

    [Fact]
    public async Task AWebsiteThatEqualsTheRosterUrlIsSavedWithNoWarningBecauseTheSavedWebsiteIsTheOneThatPublishes()
    {
        await SeedRosterAsync(member => member);

        var result = await SetWebsiteAsync("maria-cantwell", "https://www.cantwell.senate.gov");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Single(await ReadSettingsAsync());
    }

    [Fact]
    public async Task APhotoCommandDoesNotReadTheCollectionConfigurationSoABadPathDoesNotFailIt()
    {
        await SeedPresidentAsync();

        var result = await HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string>(Owner) { ["CIVIC_LENS_COLLECTION_CONFIG"] = Path.Combine(Path.GetTempPath(), "missing-collection.json") },
            "people", "photo", "withhold", "donald-trump");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(await ReadSettingsAsync());
    }

    [Fact]
    public async Task AWebsiteWithNoCollectionConfigurationIsSavedWithNoWarning()
    {
        await SeedRosterAsync(member => member);

        var result = await HostProcess.RunWithEnvironmentAsync(connectionString, null, Owner, "people", "website", "set", "maria-cantwell", "https://example.gov/");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Single(await ReadSettingsAsync());
    }

    private async Task SeedRosterAsync(Func<CivicLens.Application.Roster.RosterMember, CivicLens.Application.Roster.RosterMember> change)
    {
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(RosterFixtures.Configuration(
            RosterFixtures.Person("maria-cantwell", "Maria Cantwell", "C000127"),
            RosterFixtures.Person("no-bioguide", "No Bioguide", null),
            RosterFixtures.Person("former-member", "Former Member", "F000001")), ConfigurationJson.Options));
        await PersonSeed.InsertAsync(connectionString, "maria-cantwell", "C000127");
        await PersonSeed.InsertAsync(connectionString, "no-bioguide");
        await PersonSeed.InsertAsync(connectionString, "former-member", "F000001");
        var attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var attemptId = await RosterFixtures.ImportCaptureAsync(attempts, new string('a', 64), DateTimeOffset.UtcNow);
        await PostgresMemberRosterStore.FromConnectionString(connectionString)
            .AdmitAsync(attemptId, [.. RosterFixtures.Members().Select(change)], null, default);
    }

    private Task<(int ExitCode, string Output, string Error)> SetWebsiteAsync(string personId, string url = "https://example.gov/") =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string>(Owner) { ["CIVIC_LENS_COLLECTION_CONFIG"] = configurationPath },
            "people", "website", "set", personId, url);

    [Theory]
    [InlineData("people", "photo", "withhold", "no-such-person")]
    [InlineData("people", "website", "set", "donald-trump", "http://www.whitehouse.gov/")]
    [InlineData("people", "website", "set", "donald-trump", "https://user@www.whitehouse.gov/")]
    public async Task AnUnknownPersonOrAnInvalidUrlExitsWithTwoAndWritesNothing(params string[] arguments)
    {
        await SeedPresidentAsync();

        var result = await RunAsync(arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(await ReadSettingsAsync());
    }

    [Fact]
    public async Task ASettingChangeNeedsTheOwnerVariableSoNoAnonymousWriteReachesTheRegistry()
    {
        await SeedPresidentAsync();

        var result = await HostProcess.RunAsync(connectionString, "people", "photo", "withhold", "donald-trump");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Invalid input/configuration", result.Error, StringComparison.Ordinal);
        Assert.Empty(await ReadSettingsAsync());
    }

    private Task SeedPresidentAsync() => PersonSeed.InsertAsync(connectionString, "donald-trump", wikidataId: "Q22686");

    private Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null, Owner, arguments);

    private async Task<List<(int, bool, string?, string)>> ReadSettingsAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT revision_number, withhold_photo, website, note, actor_subject FROM person_setting_revisions ORDER BY registry_sequence", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(int, bool, string?, string)>();
        while (await reader.ReadAsync())
        {
            Assert.Equal("owner-subject", reader.GetString(4));
            rows.Add((reader.GetInt32(0), reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
        }
        return rows;
    }
}
