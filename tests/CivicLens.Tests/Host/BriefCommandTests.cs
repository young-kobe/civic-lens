using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BriefCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task RedraftQueuesTheWeekOnceAndReportsZeroWhenItIsAlreadyPending()
    {
        var first = await RunAsync("auth0|owner", "brief", "redraft", "--week", "2026-10-05");
        var again = await RunAsync("auth0|owner", "brief", "redraft", "--week", "2026-10-05");

        Assert.Equal((0, """{"requeued":1}"""), (first.ExitCode, first.Output.Trim()));
        Assert.Equal((0, """{"requeued":0}"""), (again.ExitCode, again.Output.Trim()));
    }

    [Theory]
    [InlineData("brief")]
    [InlineData("brief", "redraft")]
    [InlineData("brief", "redraft", "--week")]
    [InlineData("brief", "redraft", "2026-10-05")]
    [InlineData("brief", "status")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use brief redraft --week", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-10-06")]
    [InlineData("10/05/2026")]
    [InlineData("not-a-date")]
    public async Task AWeekThatIsNotAMondayDateIsInvalidInput(string week)
    {
        var result = await RunAsync("auth0|owner", "brief", "redraft", "--week", week);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task TheAiDrafterCannotActAsTheOwner()
    {
        var result = await RunAsync("civic-lens:analysis", "brief", "redraft", "--week", "2026-10-05");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("The configured actor is not the owner.", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string owner, params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = owner }, arguments);
}
