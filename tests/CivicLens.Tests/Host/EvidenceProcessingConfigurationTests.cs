namespace CivicLens.Tests.Host;

public sealed class EvidenceProcessingConfigurationTests
{
    private const string Database = "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1";

    [Theory]
    [InlineData("1", 1)]
    [InlineData("4", 1)]
    [InlineData("0", 2)]
    [InlineData("5", 2)]
    [InlineData("-1", 2)]
    [InlineData("two", 2)]
    [InlineData("1.5", 2)]
    public Task TheWorkerAcceptsOnlyAWholeEvidenceConcurrencyFromOneToFour(string value, int exitCode) =>
        AssertWorkerExitAsync("CIVIC_LENS_EVIDENCE_CONCURRENCY", value, exitCode);

    [Theory]
    [InlineData("1", 1)]
    [InlineData("4", 1)]
    [InlineData("0", 2)]
    [InlineData("5", 2)]
    [InlineData("-1", 2)]
    [InlineData("two", 2)]
    [InlineData("1.5", 2)]
    public Task TheWorkerAcceptsOnlyAWholeCollectionConcurrencyFromOneToFour(string value, int exitCode) =>
        AssertWorkerExitAsync("CIVIC_LENS_COLLECTION_CONCURRENCY", value, exitCode);

    private static async Task AssertWorkerExitAsync(string variable, string value, int exitCode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, """
                {"version":1,"people":[{"id":"person","name":"Fixture"}],"sources":[
                  {"id":"source","coverage":[{"personId":"person"}],"url":"https://example.test/page",
                   "allowedOrigin":"https://example.test","allowedPathPrefix":"/page"}]}
                """);
            var environment = new Dictionary<string, string>
            {
                ["CIVIC_LENS_COLLECTION_CONFIG"] = config,
                [variable] = value
            };

            var result = await HostProcess.RunWithEnvironmentAsync(Database, null, environment,
                "worker", Path.Combine(directory, "collector.dll"), Path.Combine(directory, "captures"), "--once");

            Assert.Equal(exitCode, result.ExitCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
