using System.Text.Json;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PeopleCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string ShippedConfiguration = Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json");
    private static readonly Dictionary<string, string> Owner = new() { ["CIVIC_LENS_REVIEW_OWNER"] = "owner-subject" };
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ImportKeepsEveryShippedPersonIdAndKeyAndALaterRunAddsNothing()
    {
        var expected = JsonDocument.Parse(await File.ReadAllTextAsync(ShippedConfiguration)).RootElement.GetProperty("people").EnumerateArray()
            .Select(person => (person.GetProperty("id").GetString()!, Key(person, "bioguideId"), Key(person, "wikidataId"))).OrderBy(person => person.Item1, StringComparer.Ordinal).ToList();

        var first = await ImportAsync();
        var second = await ImportAsync();

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(expected.Count, JsonDocument.Parse(first.Output).RootElement.GetProperty("added").GetInt32());
        Assert.Equal(expected, await ReadPeopleAsync());
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(0, JsonDocument.Parse(second.Output).RootElement.GetProperty("added").GetInt32());
        Assert.Equal(expected, await ReadPeopleAsync());
    }

    [Theory]
    [InlineData("people")]
    [InlineData("people", "import")]
    [InlineData("people", "export", "x")]
    [InlineData("people", "photo", "withhold")]
    [InlineData("people", "photo", "withhold", "--note")]
    [InlineData("people", "photo", "restore", "--note")]
    [InlineData("people", "photo", "hide", "mayor")]
    [InlineData("people", "photo", "restore", "mayor", "extra")]
    [InlineData("people", "website", "set", "mayor")]
    [InlineData("people", "website", "clear", "mayor", "https://example.gov/")]
    [InlineData("people", "website", "clear", "mayor", "--note")]
    [InlineData("people", "import", "config.json", "--note", "x")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await HostProcess.RunWithEnvironmentAsync(connectionString, null, Owner, arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use people import", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithholdRestoreAndWebsiteEachAddOneRevisionWithTheOwnerAndTheNote()
    {
        await ImportAsync();

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

    [Theory]
    [InlineData("people", "photo", "withhold", "no-such-person")]
    [InlineData("people", "website", "set", "donald-trump", "http://www.whitehouse.gov/")]
    [InlineData("people", "website", "set", "donald-trump", "https://user@www.whitehouse.gov/")]
    public async Task AnUnknownPersonOrAnInvalidUrlExitsWithTwoAndWritesNothing(params string[] arguments)
    {
        await ImportAsync();

        var result = await RunAsync(arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(await ReadSettingsAsync());
    }

    [Fact]
    public async Task ASettingChangeNeedsTheOwnerVariableSoNoAnonymousWriteReachesTheRegistry()
    {
        await ImportAsync();

        var result = await HostProcess.RunAsync(connectionString, "people", "photo", "withhold", "donald-trump");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Invalid input/configuration", result.Error, StringComparison.Ordinal);
        Assert.Empty(await ReadSettingsAsync());
    }

    [Fact]
    public async Task ImportNeedsTheOwnerVariableSoNoAnonymousWriteReachesTheRegistry()
    {
        var result = await HostProcess.RunAsync(connectionString, "people", "import", ShippedConfiguration);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Invalid input/configuration", result.Error, StringComparison.Ordinal);
        Assert.Empty(await ReadPeopleAsync());
    }

    private Task<(int ExitCode, string Output, string Error)> ImportAsync() =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null, Owner, "people", "import", ShippedConfiguration);

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

    private static string? Key(JsonElement person, string name) => person.TryGetProperty(name, out var value) ? value.GetString() : null;

    private async Task<List<(string, string?, string?)>> ReadPeopleAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT person_id, bioguide_id, wikidata_id FROM people ORDER BY person_id COLLATE \"C\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string?, string?)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return rows;
    }
}
