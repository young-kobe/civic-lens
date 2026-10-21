namespace CivicLens.Tests.Host;

public sealed class ReportPullConfigurationTests
{
    private const string Database = "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1";
    private const string ShortToken = "short-token-sentinel";
    private const string GoodToken = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("yes", "http://search.example.test", GoodToken, 2)]
    [InlineData("1", null, GoodToken, 2)]
    [InlineData("1", "ftp://search.example.test", GoodToken, 2)]
    [InlineData("1", "not a url", GoodToken, 2)]
    [InlineData("1", "http://search.example.test", null, 2)]
    [InlineData("1", "http://search.example.test", ShortToken, 2)]
    [InlineData("1", "http://search.example.test", GoodToken, 1)]
    public async Task TheWorkerStopsAtStartupWhenTheReportPullIsSwitchedOnWithoutAUsableAddressAndTokenAndNeverPrintsTheToken(
        string enabled, string? url, string? token, int exitCode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-report-pull-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, """
                {"version":2,"people":[{"id":"person","name":"Fixture"}],"sources":[
                  {"id":"source","coverage":[{"personId":"person"}],"url":"https://example.test/page",
                   "allowedOrigin":"https://example.test","allowedPathPrefix":"/page"}]}
                """);
            var environment = new Dictionary<string, string>
            {
                ["CIVIC_LENS_COLLECTION_CONFIG"] = config,
                ["CIVIC_LENS_REPORT_PULL"] = enabled
            };
            if (url is not null) environment["CIVIC_LENS_REPORT_PULL_URL"] = url;
            if (token is not null) environment["CIVIC_LENS_REPORT_PULL_TOKEN"] = token;

            var result = await HostProcess.RunWithEnvironmentAsync(Database, null, environment,
                "worker", Path.Combine(directory, "collector.dll"), Path.Combine(directory, "captures"), "--once");

            Assert.Equal(exitCode, result.ExitCode);
            Assert.DoesNotContain(ShortToken, result.Error + result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
