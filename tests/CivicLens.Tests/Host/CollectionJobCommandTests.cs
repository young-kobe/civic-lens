using System.Text.Json;
using CivicLens.Application.Configuration;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class CollectionJobCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string configurationPath = Path.Combine(Path.GetTempPath(), "job-config-" + Guid.NewGuid().ToString("N") + ".json");
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public async Task DisposeAsync()
    {
        File.Delete(configurationPath);
        await postgres.DropDatabaseAsync(connectionString);
    }

    // A scheduled check takes no key, date, or file, so the command must refuse instead of ignoring them.
    [Fact]
    public async Task JobsEnqueueRefusesABillListSource()
    {
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(CongressBillSourceConfigurationTests.Configuration(), ConfigurationJson.Options));

        var result = await HostProcess.RunAsync(connectionString, "jobs", "enqueue", configurationPath, "congress-bills", "key-1");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("collected by its schedule", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }
}
