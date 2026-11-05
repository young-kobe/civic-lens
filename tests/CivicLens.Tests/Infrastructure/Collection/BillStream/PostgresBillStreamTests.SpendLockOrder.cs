using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    [Fact]
    public async Task ACodeApprovedSettlementTakesTheMonthRowLastSoAReservationThatHoldsTheRelationshipLockNeverDeadlocksWithIt()
    {
        _ = await ReadableCandidateAsync("A000001", UpdatedAt);
        await using var other = new NpgsqlConnection(connectionString);
        await other.OpenAsync();
        await using var transaction = await other.BeginTransactionAsync();
        await RunAsync(other, "SELECT pg_advisory_xact_lock(hashtextextended('civic-lens-relationship-review', 0))");

        var worker = RunActionWorkerAsync(new FakeHeadlineSummaryModel().Draft(DraftableOutput));
        await WaitForAdvisoryWaiterAsync();
        await RunAsync(other, "SELECT 1 FROM ai_month_spend FOR UPDATE", TimeSpan.FromSeconds(10));
        await transaction.CommitAsync();

        await worker.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("Approved", await ScalarAsync("SELECT review_status FROM action_drafts"));
    }

    private async Task WaitForAdvisoryWaiterAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (Convert.ToInt64(await ScalarAsync("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted")) == 0)
            await Task.Delay(50, timeout.Token);
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql, TimeSpan? timeout = null)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = (int)(timeout ?? TimeSpan.FromSeconds(30)).TotalSeconds };
        _ = await command.ExecuteScalarAsync();
    }
}
