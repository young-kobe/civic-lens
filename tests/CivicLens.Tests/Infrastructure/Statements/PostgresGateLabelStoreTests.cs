using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Statements;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Statements;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresGateLabelStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Owner = "auth0|owner";
    private static readonly string RunId = new('b', 32);
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-gate-labels-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresGateLabelStore store = null!;
    private StatementCandidateSeeder seeder = null!;
    private int seeded;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        store = new PostgresGateLabelStore(factory);
        seeder = new StatementCandidateSeeder(new PostgresCollectionAttemptStore(factory), new PostgresDocumentExtractionStore(factory),
            connectionString, root);
        Directory.CreateDirectory(root);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task LabelsAreAppendedAndTheNewestHumanRowIsTheLabelOfRecordSoACorrectionNeverEditsHistory()
    {
        var candidate = await SeedAsync(heldOut: false);
        await store.AddAsync(await EntryAsync(candidate, ReviewAuthorKind.Person, ["rent"], false, 0), default);
        await store.AddAsync(await EntryAsync(candidate, ReviewAuthorKind.Person, ["rent", "health"], true, 1), default);

        var task = (await store.GetAsync(candidate, default))!;

        Assert.Equal("rent,health:True", Shape(task.HumanLabel));
        Assert.Equal(2L, await CountAsync("SELECT count(*) FROM statement_gate_labels WHERE candidate_id = @id", candidate));
    }

    [Fact]
    public async Task AModelLabelNeverReplacesTheHumanLabelOfRecord()
    {
        var candidate = await SeedAsync(heldOut: false);
        await store.AddAsync(await EntryAsync(candidate, ReviewAuthorKind.Person, ["rent"], false, 0), default);
        await GateLabelRuns.InsertAsync(connectionString, candidate, RunId);
        await store.AddAsync(await EntryAsync(candidate, ReviewAuthorKind.Analysis, ["health"], true, 1, RunId), default);

        var task = (await store.GetAsync(candidate, default))!;

        Assert.Equal(("rent:False", "health:True"), (Shape(task.HumanLabel), Shape(task.ModelLabel)));
        var exported = Assert.Single(await store.ListLabelsOfRecordAsync(default));
        Assert.Equal("rent:False", Shape(new GateLabel(exported.IssueIds, exported.NamesBillOrVote)));
    }

    [Fact]
    public async Task AHeldOutReleaseRefusesAModelLabelAndStoresNothingSoTheTestSetStaysBlindToModels()
    {
        var candidate = await SeedAsync(heldOut: true);
        var entry = await EntryAsync(candidate, ReviewAuthorKind.Analysis, ["rent"], false, 0, RunId);

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(entry, default));

        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM statement_gate_labels WHERE candidate_id = @id", candidate));
        await store.AddAsync(entry with { AuthorKind = ReviewAuthorKind.Person, RunId = null }, default);
        Assert.Null((await store.GetAsync(candidate, default))!.ModelLabel);
    }

    [Fact]
    public async Task AReleaseOutsideTheHeldOutSetTakesAModelLabelAndOffersItAsThePrefill()
    {
        var candidate = await SeedAsync(heldOut: false);

        await GateLabelRuns.InsertAsync(connectionString, candidate, RunId);
        await store.AddAsync(await EntryAsync(candidate, ReviewAuthorKind.Analysis, ["rent"], true, 0, RunId), default);

        var task = (await store.GetAsync(candidate, default))!;
        Assert.Equal((false, "rent:True", null), (task.HeldOut, Shape(task.ModelLabel), Shape(task.HumanLabel)));
    }

    [Fact]
    public async Task AModelRowThatReachesAHeldOutCandidateByRawSqlIsStillNeverReturnedSoTheReadKeepsTheTestSetBlind()
    {
        var candidate = await SeedAsync(heldOut: true);
        var extraction = (await store.GetAsync(candidate, default))!.ExtractionId;
        await GateLabelRuns.InsertAsync(connectionString, candidate, RunId);
        var json = """{"issueIds":["rent"],"namesBillOrVote":true}""";
        await ExecuteAsync($"""
            INSERT INTO statement_gate_labels (label_id, candidate_id, extraction_id, author_subject, author_kind, run_id, label_json, created_at_utc_ticks)
            VALUES ('{new string('d', 32)}', '{candidate}', '{extraction}', 'civic-lens:analysis', 'Analysis', '{RunId}', '{json}', 0)
            """);

        var task = (await store.GetAsync(candidate, default))!;

        Assert.Null(task.ModelLabel);
    }

    [Fact]
    public async Task AModelLabelNeedsARealRunAndEachRunLabelsOnlyOneRelease()
    {
        var first = await SeedAsync(heldOut: false);
        var second = await SeedAsync(heldOut: false);
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await store.AddAsync(await EntryAsync(first, ReviewAuthorKind.Analysis, [], false, 0, new string('e', 32)), default));
        await GateLabelRuns.InsertAsync(connectionString, first, RunId);
        await store.AddAsync(await EntryAsync(first, ReviewAuthorKind.Analysis, [], false, 0, RunId), default);

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await store.AddAsync(await EntryAsync(second, ReviewAuthorKind.Analysis, [], false, 0, RunId), default));
    }

    [Fact]
    public async Task ALabelThatCitesAnotherExtractionOrAnUnknownCandidateIsRefused()
    {
        var candidate = await SeedAsync(heldOut: false);
        var entry = await EntryAsync(candidate, ReviewAuthorKind.Person, [], false, 0);

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(entry with { ExtractionId = new string('9', 64) }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(entry with { CandidateId = new string('9', 64) }, default));

        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM statement_gate_labels", null));
    }

    [Fact]
    public async Task TheQueueListsHeldOutReleasesFirstAndDropsOnlyReleasesThatHaveAHumanLabel()
    {
        var open = await SeedAsync(heldOut: false);
        var heldOut = await SeedAsync(heldOut: true);
        var labeled = await SeedAsync(heldOut: false);
        var modelOnly = await SeedAsync(heldOut: false);
        await store.AddAsync(await EntryAsync(labeled, ReviewAuthorKind.Person, [], false, 0), default);
        await GateLabelRuns.InsertAsync(connectionString, modelOnly, RunId);
        await store.AddAsync(await EntryAsync(modelOnly, ReviewAuthorKind.Analysis, [], false, 0, RunId), default);

        var queue = await store.ListUnlabeledAsync(100, default);

        Assert.DoesNotContain(queue.Items, item => item.CandidateId == labeled);
        Assert.Contains(queue.Items, item => item.CandidateId == modelOnly);
        Assert.Equal(queue.Items.Length, queue.UnlabeledCount);
        var heldFlags = queue.Items.Select(item => item.HeldOut).ToArray();
        Assert.Equal(heldFlags.OrderByDescending(flag => flag), heldFlags);
        Assert.Contains(heldOut, queue.Items.Where(item => item.HeldOut).Select(item => item.CandidateId));
        Assert.Contains(open, queue.Items.Where(item => !item.HeldOut).Select(item => item.CandidateId));
        Assert.Single((await store.ListUnlabeledAsync(1, default)).Items);
    }

    [Fact]
    public async Task TheExportHoldsOnlyLabeledReleasesWithTheirTextTheirExtractionAndTheirSplitFlag()
    {
        var labeled = await SeedAsync(heldOut: true);
        await SeedAsync(heldOut: false);
        await store.AddAsync(await EntryAsync(labeled, ReviewAuthorKind.Person, ["rent"], true, 0), default);

        var row = Assert.Single(await store.ListLabelsOfRecordAsync(default));

        var task = (await store.GetAsync(labeled, default))!;
        Assert.Equal((labeled, task.ExtractionId, true, "rent:True", task.Text),
            (row.CandidateId, row.ExtractionId, row.HeldOut, Shape(new GateLabel(row.IssueIds, row.NamesBillOrVote)), row.Text));
    }

    private static string? Shape(GateLabel? label) => label is null ? null : $"{string.Join(',', label.IssueIds)}:{label.NamesBillOrVote}";

    private async Task<string> SeedAsync(bool heldOut)
    {
        while (true)
        {
            var number = ++seeded;
            var candidate = await seeder.SeedAsync($"The office announced a rent cap. Release {number}.", $"https://example.test/pages/{number}");
            if (GateLabelSplit.IsHeldOut(candidate) == heldOut) return candidate;
        }
    }

    private async Task<GateLabelEntry> EntryAsync(string candidate, ReviewAuthorKind kind, string[] issues, bool namesBillOrVote,
        int minutes, string? runId = null) =>
        new(candidate, (await store.GetAsync(candidate, default))!.ExtractionId, kind == ReviewAuthorKind.Person ? Owner : ReviewAuthor.AnalysisSubject,
            kind, runId, new GateLabel([.. issues], namesBillOrVote), Start.AddMinutes(minutes));

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string sql, string? candidate)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (candidate is not null) command.Parameters.AddWithValue("id", candidate);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
