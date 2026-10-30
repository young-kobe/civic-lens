using System.Text.Json;
using CivicLens.Application.Configuration;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

public sealed class ConfigurationFileTests
{
    private const int Limit = 1_000_000;
    private const string Valid = """
        {"version":1,"people":[{"id":"person","name":"Fixture"}],"sources":[
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
            Assert.Equal(exitCode != 0, result.Error.Contains("1 MB limit", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TheLiveFileRefusesAnEnabledSenateGovSourceBecauseItsReaderIsGoneAndStoredRevisionsMustKeepLoading()
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-config-senate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var enabled = Path.Combine(directory, "enabled.json");
            var disabled = Path.Combine(directory, "disabled.json");
            var configuration = VoteviewVoteFixtures.RetiredSenateGovConfiguration();
            await File.WriteAllTextAsync(enabled, JsonSerializer.Serialize(configuration, ConfigurationJson.Options));
            await File.WriteAllTextAsync(disabled, JsonSerializer.Serialize(
                configuration with { Sources = [configuration.Sources.Single() with { Enabled = false }] }, ConfigurationJson.Options));

            Assert.Equal(0, (await HostProcess.RunAsync(null, "validate", disabled)).ExitCode);
            Assert.Equal(2, (await HostProcess.RunAsync(null, "validate", enabled)).ExitCode);
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
