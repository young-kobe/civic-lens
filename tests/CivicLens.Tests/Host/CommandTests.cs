namespace CivicLens.Tests.Host;

public sealed class CommandTests
{
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
