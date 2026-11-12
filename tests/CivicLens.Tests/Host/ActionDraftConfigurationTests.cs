using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

public sealed class ActionDraftConfigurationTests
{
    private const string Database = "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1";
    private const string BillTextVariable = "CIVIC_LENS_ACTION_DRAFTING_BILL_TEXT";
    private const string KeyTermsVariable = "CIVIC_LENS_ACTION_DRAFTING_KEY_TERMS";

    [Theory]
    [InlineData("0", 2)]
    [InlineData("2", 2)]
    [InlineData("true", 2)]
    [InlineData("1", 1)]
    public async Task TheWorkerRefusesABillTextSettingOtherThanOneBecauseTheSettingGatesAPublicTextSource(string value, int exitCode)
    {
        var result = await RunWorkerAsync(new() { [BillTextVariable] = value });

        Assert.Equal(exitCode, result.ExitCode);
    }

    [Theory]
    [InlineData("0", "1", 2)]
    [InlineData("true", "1", 2)]
    [InlineData("1", null, 2)]
    [InlineData("1", "0", 2)]
    [InlineData("1", "1", 1)]
    [InlineData(null, "1", 1)]
    public async Task TheWorkerRefusesAKeyTermSettingOtherThanOneOrOneWithNoBillTextSettingBecauseOnlyABillTextDraftHasKeyTerms(string? keyTerms,
        string? billText, int exitCode)
    {
        var settings = new Dictionary<string, string>();
        if (keyTerms is not null) settings[KeyTermsVariable] = keyTerms;
        if (billText is not null) settings[BillTextVariable] = billText;

        var result = await RunWorkerAsync(settings);

        Assert.Equal(exitCode, result.ExitCode);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunWorkerAsync(Dictionary<string, string> settings)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-bill-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, """
                {"version":1,"people":[{"id":"person","name":"Fixture"}],"sources":[
                  {"id":"source","coverage":[{"personId":"person"}],"url":"https://example.test/page",
                   "allowedOrigin":"https://example.test","allowedPathPrefix":"/page"}]}
                """);
            var environment = new Dictionary<string, string>(settings)
            {
                ["CIVIC_LENS_COLLECTION_CONFIG"] = config,
                ["CIVIC_LENS_ISSUES_CONFIG"] = Path.Combine(RepositoryRoot.FullName, "config", "issues.json"),
                ["CIVIC_LENS_ACTION_DRAFTING_DAILY_TOKENS"] = "2000000",
                ["ANTHROPIC_API_KEY"] = "key-sentinel"
            };

            var result = await HostProcess.RunWithEnvironmentAsync(Database, null, environment,
                "worker", Path.Combine(directory, "collector.dll"), Path.Combine(directory, "captures"), "--once");

            Assert.DoesNotContain("key-sentinel", result.Output + result.Error);
            return result;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
