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
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await HostProcess.RunWithEnvironmentAsync(connectionString, null, Owner, arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use people import", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportNeedsTheOwnerVariableSoNoAnonymousWriteReachesTheRegistry()
    {
        var result = await HostProcess.RunAsync(connectionString, "people", "import", ShippedConfiguration);

        Assert.NotEqual(0, result.ExitCode);
    }

    private Task<(int ExitCode, string Output, string Error)> ImportAsync() =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null, Owner, "people", "import", ShippedConfiguration);

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
