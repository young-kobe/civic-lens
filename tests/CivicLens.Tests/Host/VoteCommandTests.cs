using System.Text.Json;
using CivicLens.Application.Votes;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task StatusReportsZeroCountsForEveryOutcomeAndZeroWaitingVotesBeforeAnyCandidateExists()
    {
        var result = await RunAsync("auth0|owner", "votes", "draft", "status");

        Assert.Equal(0, result.ExitCode);
        using var status = JsonDocument.Parse(result.Output);
        var queue = status.RootElement.GetProperty("queue");
        Assert.Contains("chargedToday", queue.EnumerateObject().Select(property => property.Name));
        foreach (var view in new[] { "pending", "drafted", "unchanged", "superseded", "noTrackedMember", "failed" })
            Assert.Equal(0, queue.GetProperty("counts").GetProperty(view).GetInt32());
        Assert.Equal(0, status.RootElement.GetProperty("waitingForBill").GetInt32());
    }

    [Theory]
    [InlineData("drafted")]
    [InlineData("unchanged")]
    [InlineData("superseded")]
    [InlineData("no-member")]
    [InlineData("pending")]
    [InlineData("failed")]
    public async Task ListAcceptsEachOutcomeFilter(string outcome)
    {
        var result = await RunAsync("auth0|owner", "votes", "draft", "list", outcome, "5");

        Assert.Equal((0, "[]"), (result.ExitCode, result.Output.Trim()));
    }

    [Theory]
    [InlineData("votes")]
    [InlineData("votes", "draft")]
    [InlineData("votes", "draft", "list", "passed")]
    [InlineData("votes", "draft", "requeue")]
    [InlineData("votes", "draft", "requeue", "--blocked")]
    [InlineData("votes", "draft", "requeue", "--blocked", "--code")]
    [InlineData("votes", "draft", "run", "x")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use votes draft status", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequeueReportsZeroWhenNothingFailedAndRejectsAnIdThatIsNotAHash()
    {
        var all = await RunAsync("auth0|owner", "votes", "draft", "requeue", "--all");
        var bad = await RunAsync("auth0|owner", "votes", "draft", "requeue", "not-a-hash");

        Assert.Equal((0, """{"requeued":0}"""), (all.ExitCode, all.Output.Trim()));
        Assert.Equal(2, bad.ExitCode);
    }

    [Fact]
    public async Task RequeueBlockedReportsTheCountAndRefusesAnEmptyCodeSoItNeverMatchesEveryBlockedRow()
    {
        var known = await RunAsync("auth0|owner", "votes", "draft", "requeue", "--blocked", "--code", VoteDraftErrorCodes.FactTooLong);
        var empty = await RunAsync("auth0|owner", "votes", "draft", "requeue", "--blocked", "--code", " ");

        Assert.Equal((0, """{"requeued":0}"""), (known.ExitCode, known.Output.Trim()));
        Assert.Equal(2, empty.ExitCode);
        Assert.Contains("errorCode", empty.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedraftReportsZeroWhenNoDraftCameFromAnOlderPrompt()
    {
        var result = await RunAsync("auth0|owner", "votes", "draft", "redraft");

        Assert.Equal((0, """{"requeued":0}"""), (result.ExitCode, result.Output.Trim()));
    }

    [Theory]
    [InlineData("redraft")]
    [InlineData("requeue", "--all")]
    [InlineData("requeue", "--blocked", "--code", VoteDraftErrorCodes.FactTooLong)]
    public async Task TheAiDrafterCannotActAsTheOwner(params string[] command)
    {
        var result = await RunAsync("civic-lens:analysis", ["votes", "draft", .. command]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("The configured actor is not the owner.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevisionBackfillAndVerifyReportZeroOnAnEmptyDatabaseSoTheDocumentedCommandsRun()
    {
        var backfill = await RunAsync("auth0|owner", "votes", "revisions", "backfill", "5");
        var verify = await RunAsync("auth0|owner", "votes", "revisions", "verify");

        Assert.Equal(0, backfill.ExitCode);
        Assert.Contains("\"examined\":0", backfill.Output, StringComparison.Ordinal);
        Assert.Equal(0, verify.ExitCode);
        Assert.Contains("\"mismatched\":0", verify.Output, StringComparison.Ordinal);
    }

    // The rows hold every column, and the JSON is no revision, so the rebuild cannot equal it.
    [Fact]
    public async Task VerifyExitsWithOneAndNamesTheMismatchWhenARowDiffersFromItsJsonSoTheOwnerCannotMissIt()
    {
        await InsertRevisionAsync("{}", withColumns: true);

        var result = await RunAsync("auth0|owner", "votes", "revisions", "verify");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("\"mismatched\":1", result.Output, StringComparison.Ordinal);
        Assert.Contains("recordId", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyExitsWithOneWhileALegacyRowRemainsBecauseAReadFailsOnNullColumns()
    {
        await InsertRevisionAsync("x", withColumns: false);

        var result = await RunAsync("auth0|owner", "votes", "revisions", "verify");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("\"legacy\":1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackfillExitsWithOneOnASkippedRowAndStillPrintsTheCountsSoTheOwnerSeesWhatHappened()
    {
        await InsertRevisionAsync("x", withColumns: false);

        var result = await RunAsync("auth0|owner", "votes", "revisions", "backfill", "5");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("\"skipped\":1", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"UnreadableJson\":1", result.Output, StringComparison.Ordinal);
    }

    private async Task InsertRevisionAsync(string revisionJson, bool withColumns)
    {
        var record = new string('a', 64);
        var columns = withColumns
            ? $$"""
                , headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
                candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length, kind,
                member_list_id, issue_revision_id, issue_candidates_recorded
                """
            : "";
        var values = withColumns
            ? $$"""
                , 'H', 'S.', 'Person', 'house-clerk', 'CongressGov', 'House', 119, 1, 100,
                '{{record}}', 100, '2025-04-10', '{{record}}', 0, 1, 'Other', '{{record}}', '{{record}}', false
                """
            : "";
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            SET session_replication_role = replica;
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, candidate_id,
                current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{{record}}', 'house-clerk', 'House', 119, 1, 100, '{{record}}', 1, 0, 'AwaitingReview', 100);
            INSERT INTO vote_member_lists (list_id, member_count, created_at_utc_ticks) VALUES ('{{record}}', 1, 100);
            INSERT INTO vote_member_list_entries (list_id, position, person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
            VALUES ('{{record}}', 0, 'p', 'A000001', 'Yea', '{{record}}', 0, 1);
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks, result{{columns}})
            VALUES ('{{record}}', 1, 'auth0|editor', 'Person', NULL, '{{revisionJson}}', 100, 'Passed'{{values}})
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData("votes", "revisions", "backfill")]
    [InlineData("votes", "revisions", "backfill", "many")]
    [InlineData("votes", "revisions", "backfill", "0")]
    [InlineData("votes", "revisions", "verify", "now")]
    public async Task RevisionCommandsRefuseABadLimitOrExtraArgument(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
    }

    [Theory]
    [InlineData("backfill", "5")]
    [InlineData("verify")]
    public async Task TheAiDrafterCannotRunTheRevisionCommands(params string[] command)
    {
        var result = await RunAsync("civic-lens:analysis", ["votes", "revisions", .. command]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("The configured actor is not the owner.", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string owner, params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = owner }, arguments);
}
