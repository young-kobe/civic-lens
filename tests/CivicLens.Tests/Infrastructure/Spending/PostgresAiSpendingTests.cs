using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Spending;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Infrastructure.Spending;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresAiSpendingTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Haiku = "claude-haiku-5-5";
    private const string Owner = "auth0|owner";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-spending-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresAiSpendingStore spending = null!;
    private PostgresDocumentChangeAnalysisStore analysis = null!;
    private PostgresRelationshipDraftStore relationships = null!;
    private RelationshipWorldSeeder relationshipSeeder = null!;
    private ComparisonSeeder seeder = null!;
    private int seeded;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        spending = new PostgresAiSpendingStore(factory);
        analysis = new PostgresDocumentChangeAnalysisStore(factory);
        relationships = new PostgresRelationshipDraftStore(factory);
        relationshipSeeder = new RelationshipWorldSeeder(connectionString);
        seeder = new ComparisonSeeder(new PostgresCollectionAttemptStore(factory), new PostgresDocumentExtractionStore(factory),
            new PostgresDocumentComparisonStore(factory), captureRoot);
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task AReservationThatWouldPassTheMonthlyCapWaitsAndOneThatFitsExactlyIsReserved()
    {
        await spending.SaveCapAsync(Owner, 24_999, default);
        var claim = await ClaimAsync();

        var over = await ReserveAsync(claim, Haiku, 5_000);

        Assert.Equal(AnalysisReservation.WaitingForBudget, over);
        Assert.Equal(("monthlyDollarCap", 0L, 0L), await EntryAndSpendAsync(claim));
        Assert.True(await RetryDelayAsync() > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, await RetryAtMinusNextMidnightAsync());
        await spending.SaveCapAsync(Owner, 25_000, default);
        await analysis.ReleaseBudgetWaitsAsync(default);
        var again = await analysis.TryClaimAsync(claim.Record.SubjectId, Lease, default);

        Assert.Equal(AnalysisReservation.Reserved, await ReserveAsync(again!, Haiku, 5_000));
        Assert.Equal(25_000, await MonthChargedAsync());
    }

    [Fact]
    public async Task WithoutACapEveryCallWaitsBecauseTheSpendingFailsClosed()
    {
        await ExecuteAsync("DELETE FROM ai_monthly_cap_revisions");
        var claim = await ClaimAsync();

        Assert.Equal(AnalysisReservation.WaitingForBudget, await ReserveAsync(claim, Haiku, 1_000));

        Assert.Equal(("monthlyCapNotSet", 0L, 0L), await EntryAndSpendAsync(claim));
        await AssertRetriesInAboutFiveMinutesAsync();
        Assert.Equal(0, await ChargedTokensTodayAsync());
    }

    [Fact]
    public async Task AModelWithoutAPriceNeverRunsBecauseItsCostIsUnknown()
    {
        var claim = await ClaimAsync();

        Assert.Equal(AnalysisReservation.WaitingForBudget, await ReserveAsync(claim, "claude-unpriced", 1_000));

        Assert.Equal(("modelPriceMissing", 0L, 0L), await EntryAndSpendAsync(claim));
        await AssertRetriesInAboutFiveMinutesAsync();
        Assert.Equal(0, await ChargedTokensTodayAsync());
    }

    [Fact]
    public async Task ConcurrentReservationsNeverPassTheMonthlyCap()
    {
        await spending.SaveCapAsync(Owner, 15_000, default);
        var claims = new List<AnalysisClaim>();
        for (var index = 0; index < 6; index++) claims.Add(await ClaimAsync());

        var results = await Task.WhenAll(claims.Select(claim => ReserveAsync(claim, Haiku, 1_000)));

        Assert.Equal(3, results.Count(result => result == AnalysisReservation.Reserved));
        Assert.Equal(15_000, await MonthChargedAsync());
    }

    [Fact]
    public async Task ReservationsOfDifferentStagesNeverPassTheMonthlyCapBecauseTheyShareOneMonthRow()
    {
        await spending.SaveCapAsync(Owner, 10_000, default);
        var first = await ClaimAsync();
        var second = await ClaimAsync();
        await relationshipSeeder.StatementAsync(Statement());
        await relationshipSeeder.ActionAsync(Action());
        _ = await relationships.RefreshPairsAsync(default);
        var pairClaim = (await relationships.TryClaimAsync(ActionPair().PairId, Lease, default))!;

        var results = await Task.WhenAll(ReserveAsync(first, Haiku, 1_000), ReserveAsync(second, Haiku, 1_000),
            relationships.ReserveAsync(pairClaim, Guid.NewGuid().ToString("N"), new string('f', 64), Haiku, 1_000, 10_000_000, default));

        Assert.Equal(2, results.Count(result => result == AnalysisReservation.Reserved));
        Assert.Equal(10_000, await MonthChargedAsync());
    }

    [Fact]
    public async Task AReservationLocksTheMonthRowBeforeItReadsTheTotalSoNoTwoStagesCanBothPassTheCap()
    {
        await spending.SaveCapAsync(Owner, 1_000_000, default);
        var claim = await ClaimAsync();
        await ExecuteAsync("INSERT INTO ai_month_spend (month_start, charged_micros) VALUES (date_trunc('month', now() AT TIME ZONE 'UTC')::date, 0)");
        await using var holder = new NpgsqlConnection(connectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var lockMonth = new NpgsqlCommand("SELECT 1 FROM ai_month_spend FOR UPDATE", holder, transaction))
            _ = await lockMonth.ExecuteScalarAsync();

        var reservation = ReserveAsync(claim, Haiku, 1_000);
        var waiting = await WaitingStatementAsync();

        Assert.Contains("FOR UPDATE", waiting, StringComparison.Ordinal);
        await transaction.CommitAsync();
        Assert.Equal(AnalysisReservation.Reserved, await reservation.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    private async Task<string> WaitingStatementAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var statement = await ScalarAsync<string?>(
                "SELECT query FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' LIMIT 1");
            if (statement is not null) return statement;
            await Task.Delay(50, timeout.Token);
        }
    }

    [Fact]
    public async Task ACapOfZeroStopsEveryCallEvenOfAModelWithAZeroPrice()
    {
        await spending.SaveCapAsync(Owner, 0, default);
        await spending.SavePriceAsync(Owner, new ModelPrice(Haiku, 0, 0, 0, 0), default);
        var claim = await ClaimAsync();

        Assert.Equal(AnalysisReservation.WaitingForBudget, await ReserveAsync(claim, Haiku, 1_000));

        Assert.Equal(("monthlyDollarCap", 0L, 0L), await EntryAndSpendAsync(claim));
    }

    [Fact]
    public async Task ASettledRunChargesItsRealCostAndGivesBackTheWorstCaseReservation()
    {
        var claim = await ClaimAsync();
        var runId = Guid.NewGuid().ToString("N");
        Assert.Equal(AnalysisReservation.Reserved, await ReserveAsync(claim, Haiku, 5_000, runId));
        Assert.Equal(25_000, await MonthChargedAsync());
        var usage = new AnalysisTokenUsage(1_000, 500, 2_000, 100);

        Assert.True(await analysis.CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Failed, "refused"),
            Run(claim.Record.SubjectId, runId, usage, usage.Total), default));

        Assert.Equal(3_825, await MonthChargedAsync());
        Assert.Equal(0, await ScalarAsync<long>("SELECT reserved_micros FROM document_change_analysis"));
    }

    [Fact]
    public async Task ACallWithNoCountedUsageStaysChargedAtTheWorstCaseBecauseItMayHaveBeenBilled()
    {
        var claim = await ClaimAsync();
        var runId = Guid.NewGuid().ToString("N");
        await ReserveAsync(claim, Haiku, 5_000, runId);

        Assert.True(await analysis.CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Failed, "refused"),
            Run(claim.Record.SubjectId, runId, AnalysisTokenUsage.None, 5_000), default));

        Assert.Equal(25_000, await MonthChargedAsync());
    }

    [Fact]
    public async Task AnInterruptedRunKeepsItsWorstCaseChargeInTheMonth()
    {
        var claim = await ClaimAsync();
        await ReserveAsync(claim, Haiku, 5_000);
        await ExecuteAsync($"UPDATE document_change_analysis SET lease_expires_at = 0 WHERE comparison_id = '{claim.Record.SubjectId}'");

        Assert.NotNull(await analysis.TryClaimAsync(claim.Record.SubjectId, Lease, default));

        Assert.Equal(25_000, await MonthChargedAsync());
        Assert.Equal(0, await ScalarAsync<long>("SELECT reserved_micros FROM document_change_analysis"));
    }

    [Fact]
    public async Task EachSaveAddsARevisionAndTheNewestOneIsCurrent()
    {
        await spending.SaveCapAsync(Owner, 5_000_000, default);
        await spending.SaveCapAsync("auth0|second", 7_000_000, default);
        await spending.SavePriceAsync(Owner, new ModelPrice(Haiku, 2, 3, 4, 5), default);
        await spending.SavePriceAsync(Owner, new ModelPrice(Haiku, 6, 7, 8, 9), default);

        var view = await spending.GetAsync(10, default);

        Assert.Equal([3, 2, 1], view.Caps.Select(cap => cap.RevisionNumber));
        Assert.Equal((7_000_000, "auth0|second"), (view.CurrentCap!.CapMicros, view.CurrentCap.AuthorSubject));
        var current = view.CurrentPrices.Single(price => price.Price.Model == Haiku);
        Assert.Equal((3, new ModelPrice(Haiku, 6, 7, 8, 9)), (current.RevisionNumber, current.Price));
        Assert.Equal([3, 2], view.PriceChanges.Where(change => change.Price.Model == Haiku).Select(change => change.RevisionNumber).Take(2));
    }

    [Fact]
    public async Task TheViewShowsTheSpendOfTheCurrentUtcMonth()
    {
        var claim = await ClaimAsync();
        await ReserveAsync(claim, Haiku, 5_000);

        var view = await spending.GetAsync(10, default);

        var month = DateOnly.FromDateTime(await ScalarAsync<DateTime>("SELECT date_trunc('month', now() AT TIME ZONE 'UTC')"));
        Assert.Equal((month, 25_000), (view.Month, view.ChargedMicros));
    }

    private async Task<AnalysisClaim> ClaimAsync()
    {
        var number = ++seeded;
        var comparison = await seeder.SaveAsync($"Item {number} old.\n", $"Item {number} new.\n");
        return (await analysis.TryClaimAsync(comparison.ComparisonId, Lease, default))!;
    }

    private Task<AnalysisReservation> ReserveAsync(AnalysisClaim claim, string model, long tokens, string? runId = null) =>
        analysis.ReserveAsync(claim, runId ?? Guid.NewGuid().ToString("N"), new string('f', 64), model, tokens, 10_000_000, default);

    private static AnalysisRun Run(string comparisonId, string runId, AnalysisTokenUsage usage, long charged) => new(runId, comparisonId,
        DocumentChangeDraftTask.Task, DocumentChangeDraftTask.TaskVersion, Haiku, DocumentChangeDraftTask.PromptVersion,
        DocumentChangeDraftTask.SchemaVersion, new string('f', 64), null, AnalysisRunOutcome.Refused, usage, charged, null, null, null,
        ImmutableArray<string>.Empty, "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, AnalysisRunInputs.Sample);

    private async Task<(string? Code, long Reserved, long Month)> EntryAndSpendAsync(AnalysisClaim claim) =>
        (await ScalarAsync<string>($"SELECT error_code FROM document_change_analysis WHERE comparison_id = '{claim.Record.SubjectId}'"),
            await ScalarAsync<long>($"SELECT reserved_micros FROM document_change_analysis WHERE comparison_id = '{claim.Record.SubjectId}'"),
            await MonthChargedAsync());

    private async Task AssertRetriesInAboutFiveMinutesAsync()
    {
        var delay = await RetryDelayAsync();
        Assert.InRange(delay, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    private async Task<TimeSpan> RetryDelayAsync() => TimeSpan.FromTicks(await ScalarAsync<long>(
        "SELECT (retry_at - ((extract(epoch FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000))::bigint FROM document_change_analysis"));

    private async Task<TimeSpan> RetryAtMinusNextMidnightAsync() => TimeSpan.FromTicks(await ScalarAsync<long>(
        "SELECT (retry_at - (((extract(epoch FROM date_trunc('day', now() AT TIME ZONE 'UTC') + interval '1 day') * 10000000)::bigint) + 621355968000000000))::bigint FROM document_change_analysis"));

    private Task<long> MonthChargedAsync() => ScalarAsync<long>("SELECT COALESCE(sum(charged_micros), 0)::bigint FROM ai_month_spend");

    private Task<long> ChargedTokensTodayAsync() =>
        ScalarAsync<long>("SELECT COALESCE(sum(charged_tokens), 0)::bigint FROM document_change_analysis_budget");

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
