namespace CivicLens.Tests.Host;

public sealed class ConfigurationFileTests
{
    private const int Limit = 1_000_000;
    private const string Valid = """
        {"version":2,"people":[{"id":"person","name":"Fixture"}],"sources":[
          {"id":"source","coverage":[{"personId":"person"}],"url":"https://example.test/page",
           "allowedOrigin":"https://example.test","allowedPathPrefix":"/page"}]}
        """;

    [Theory]
    [InlineData(Limit, 0)]
    [InlineData(Limit + 1, 2)]
    public async Task AFileOverOneMegabyteIsRejectedWhateverItsDeclaredSize(int bytes, int exitCode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-config-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(path, Valid + new string(' ', bytes - Valid.Length));

            var result = await HostProcess.RunAsync(null, "validate", path);

            Assert.Equal(exitCode, result.ExitCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ARelativeEnvironmentPathStopsTheWorkerAsInvalidConfiguration()
    {
        var result = await HostProcess.RunWithEnvironmentAsync(null, null,
            new Dictionary<string, string> { ["CIVIC_LENS_COLLECTION_CONFIG"] = "config/example.json" },
            "worker", "collector.dll", "captures", "--once");

        Assert.Equal(2, result.ExitCode);
    }
}
