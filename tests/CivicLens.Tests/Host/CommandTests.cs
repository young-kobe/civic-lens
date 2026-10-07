namespace CivicLens.Tests.Host;

public sealed class CommandTests
{
    [Fact]
    public async Task ReceiptListingIsDatabaseFreeAndReportsMalformedPendingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "civic-receipts-" + Guid.NewGuid().ToString("N"));
        var pending = Path.Combine(root, ".pending");
        Directory.CreateDirectory(pending);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(pending, "invalid.json"), "broken receipt");
            var listed = await HostProcess.RunAsync(null, "receipts", "list", root);
            Assert.Equal(0, listed.ExitCode);
            Assert.Contains("invalid", listed.Output);
            var single = await HostProcess.RunAsync(
                "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1",
                "receipts", "replay", root, "invalid");
            Assert.Equal(1, single.ExitCode);
            Assert.Empty(single.Output);
            Assert.Contains("Receipt replay failed", single.Error);
            Assert.Contains("retained handoff", single.Error);
            Assert.DoesNotContain("collector", single.Error);
            Assert.DoesNotContain("receipts replay", single.Error);
            Assert.DoesNotContain("secret-sentinel", single.Error);
            var replayed = await HostProcess.RunAsync(
                "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1",
                "receipts", "replay", root, "--all");
            Assert.Equal(1, replayed.ExitCode);
            Assert.Contains("invalidHandoff", replayed.Output);
            Assert.DoesNotContain("secret-sentinel", replayed.Output + replayed.Error);
            Assert.True(File.Exists(Path.Combine(pending, "invalid.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("jobs", "get", "job", "--as-of", "2026-07-01")]
    [InlineData("jobs", "enqueue", "config", "source", "key", "--as-of", "07/01/2026")]
    [InlineData("collect", "config", "source", "collector", "captures", "--as-of", "2026-02-30")]
    public async Task CoverageDateRejectsInvalidFormatsAndUnsupportedCommands(params string[] arguments)
    {
        var result = await HostProcess.RunAsync(null, arguments);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--as-of yyyy-MM-dd", result.Error);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Password=secret-sentinel;Invalid=secret-sentinel")]
    [InlineData("Host=localhost;Password=secret-sentinel")]
    public async Task MigrationRequiresExplicitValidConfigurationWithoutExposingValues(string? connection)
    {
        var result = await HostProcess.RunAsync(connection, "db", "migrate");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("CIVIC_LENS_DATABASE", result.Error);
        Assert.DoesNotContain("secret-sentinel", result.Error);
    }

    [Fact]
    public async Task UnreachableDatabaseIsOperationalFailureWithoutCredentialOutput()
    {
        var result = await HostProcess.RunAsync(
            "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1",
            "db", "migrate");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("secret-sentinel", result.Error);
    }

    [Fact]
    public async Task UnknownCommandsAndAttemptOverridesAreRejectedWithoutEchoingArguments()
    {
        var result = await HostProcess.RunAsync(null, "collect-import", "config", "source", "collector", "captures",
            "--attempt-id", "secret-sentinel");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("secret-sentinel", result.Error);
    }
}
