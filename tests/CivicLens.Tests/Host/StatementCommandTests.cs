using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Statements;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class StatementCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task StatusReportsZeroCountsForEveryOutcomeBeforeAnyCandidateExists()
    {
        var result = await RunAsync("auth0|owner", "statements", "status");

        Assert.Equal(0, result.ExitCode);
        foreach (var view in new[] { "pending", "passed", "skipped", "failedOpen", "overCap", "failed" })
            Assert.Contains($"\"{view}\":0", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("passed")]
    [InlineData("skipped")]
    [InlineData("failed-open")]
    [InlineData("over-cap")]
    [InlineData("pending")]
    public async Task ListAcceptsEachOutcomeFilter(string outcome)
    {
        var result = await RunAsync("auth0|owner", "statements", "list", outcome, "5");

        Assert.Equal((0, "[]"), (result.ExitCode, result.Output.Trim()));
    }

    [Theory]
    [InlineData("statements")]
    [InlineData("statements", "list", "unknown")]
    [InlineData("statements", "requeue")]
    [InlineData("statements", "regate", "--since")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use statements status", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftStatusReportsZeroCountsForEveryDraftOutcomeBeforeAnyCandidateExists()
    {
        var result = await RunAsync("auth0|owner", "statements", "draft", "status");

        Assert.Equal(0, result.ExitCode);
        foreach (var view in new[] { "pending", "drafted", "overCap", "existing", "failed" })
            Assert.Contains($"\"{view}\":0", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("drafted")]
    [InlineData("over-cap")]
    [InlineData("existing")]
    [InlineData("pending")]
    [InlineData("failed")]
    public async Task DraftListAcceptsEachOutcomeFilter(string outcome)
    {
        var result = await RunAsync("auth0|owner", "statements", "draft", "list", outcome, "5");

        Assert.Equal((0, "[]"), (result.ExitCode, result.Output.Trim()));
    }

    [Theory]
    [InlineData("statements", "draft")]
    [InlineData("statements", "draft", "list", "passed")]
    [InlineData("statements", "draft", "requeue")]
    public async Task BadDraftArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("statements draft status", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADraftRunThatDoesNotExistIsRejectedAndRequeueReportsZero()
    {
        var run = await RunAsync("auth0|owner", "statements", "draft", "run", new string('a', 32));
        var requeue = await RunAsync("auth0|owner", "statements", "draft", "requeue", "--all");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("No draft run", run.Error, StringComparison.Ordinal);
        Assert.Equal((0, """{"requeued":0}"""), (requeue.ExitCode, requeue.Output.Trim()));
    }

    [Fact]
    public async Task ARunThatDoesNotExistIsRejected()
    {
        var result = await RunAsync("auth0|owner", "statements", "run", new string('a', 32));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("No gate run", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegateNeverRunsWithoutTheIssueConfigurationThatNamesTheCurrentRevision()
    {
        var result = await RunAsync("auth0|owner", "statements", "regate");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("CIVIC_LENS_ISSUES_CONFIG", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequeueAndRegateReportZeroWhenNothingQualifies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-regate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var issues = Path.Combine(directory, "issues.json");
            await File.WriteAllTextAsync(issues, """{"version":1,"policyAreas":[],"issues":[]}""");
            var requeue = await RunAsync("auth0|owner", "statements", "requeue", "--all");
            var regate = await HostProcess.RunWithEnvironmentAsync(connectionString, null, new Dictionary<string, string>
            {
                ["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner",
                ["CIVIC_LENS_ISSUES_CONFIG"] = issues
            }, "statements", "regate", "--since", "2026-01-01");

            Assert.Equal((0, """{"requeued":0}"""), (requeue.ExitCode, requeue.Output.Trim()));
            Assert.Equal((0, """{"requeued":0}"""), (regate.ExitCode, regate.Output.Trim()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TheAiDrafterCannotActAsTheOwner()
    {
        var result = await RunAsync("civic-lens:analysis", "statements", "requeue", "--all");

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task ALabelExportOutsideTheRuntimeDirectoryIsRefusedAndWritesNoFile()
    {
        var outside = Path.Combine(Path.GetTempPath(), "civic-label-export-" + Guid.NewGuid().ToString("N") + ".jsonl");

        var result = await RunAsync("auth0|owner", "statements", "labels", "export", outside);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(".runtime", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task ALabelExportInsideTheRuntimeDirectoryWritesTheLabelOfRecordWithoutTheAuthorSubject()
    {
        var root = Path.Combine(Path.GetTempPath(), "civic-label-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var relative = Path.Combine(".runtime", "label-cli-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
                new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
            var seeder = new StatementCandidateSeeder(new PostgresCollectionAttemptStore(factory), new PostgresDocumentExtractionStore(factory),
                connectionString, root);
            var candidate = await seeder.SeedAsync("The office announced a rent cap.", "https://example.test/pages/1");
            var store = new PostgresGateLabelStore(factory);
            var extraction = (await store.GetAsync(candidate, default))!.ExtractionId;
            await store.AddAsync(new(candidate, extraction, "auth0|owner", ReviewAuthorKind.Person, null, new([], true), DateTimeOffset.UtcNow), default);

            var result = await RunAsync("auth0|owner", "statements", "labels", "export", relative);

            Assert.Equal((0, """{"exported":1}"""), (result.ExitCode, result.Output.Trim()));
            var text = await File.ReadAllTextAsync(Path.GetFullPath(relative));
            Assert.Contains(candidate, text, StringComparison.Ordinal);
            Assert.DoesNotContain("auth0|owner", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(Path.GetFullPath(relative));
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ALabelExportWithoutAPathShowsTheUsage()
    {
        var result = await RunAsync("auth0|owner", "statements", "labels", "export");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("statements labels export <path>", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string owner, params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = owner }, arguments);
}
