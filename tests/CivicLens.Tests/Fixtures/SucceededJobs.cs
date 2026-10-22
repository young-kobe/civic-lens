using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.Jobs;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

/// <summary>Seeds finished jobs through the same enqueue and admission paths that production uses.</summary>
internal static class SucceededJobs
{
    public static async Task<string> RootAsync(PostgresCollectionJobStore store, string connectionString,
        ConfiguredCollectionSource source, string key)
    {
        var job = await store.EnqueueAsync(source, key, default);
        await MarkSucceededAsync(connectionString, job.JobId);
        return job.JobId;
    }

    public static async Task<CollectionJobRecord> ScheduledBillListAsync(PostgresCollectionJobStore store,
        string connectionString, CollectionConfiguration configuration)
    {
        await store.SynchronizeAsync(configuration, default);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("UPDATE collection_source_schedules SET next_due_utc_ticks = 0", connection);
            await command.ExecuteNonQueryAsync();
        }
        await store.AdvanceDueAsync(10, default);
        return Assert.Single(await store.ListAsync(10, default));
    }

    public static async Task<string> BillDetailAsync(PostgresCollectionJobStore store, string connectionString,
        ConfiguredCollectionSource source, string listJobId, CongressBillListItem bill)
    {
        var page = new CongressBillListPage([bill], null, StartsPass: true);
        var window = new BillStreamWindow(bill.UpdateDate, bill.UpdateDate);
        var admitted = await store.AdmitListPageAsync(source, listJobId, window, page, default);
        var detail = Assert.Single(admitted.Jobs);
        await MarkSucceededAsync(connectionString, detail.JobId);
        return detail.JobId;
    }

    public static async Task<string> SenateVoteAsync(PostgresCollectionJobStore store, string connectionString,
        ConfiguredCollectionSource source, string menuKey, SenateVoteMenuItem item)
    {
        var menuJobId = await RootAsync(store, connectionString, source, menuKey);
        var admitted = await store.AdmitSenateMenuAsync(source, menuJobId, new SenateVoteMenu(item.Roll.Session, [item]), default);
        var vote = Assert.Single(admitted.Jobs, job => job.Url == SenateVoteUrl.Vote(source.SourceUrl, item.Roll));
        await MarkSucceededAsync(connectionString, vote.JobId);
        return vote.JobId;
    }

    public static async Task MarkSucceededAsync(string connectionString, string jobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE collection_jobs SET state = 'Succeeded' WHERE job_id = @id", connection);
        command.Parameters.AddWithValue("id", jobId);
        await command.ExecuteNonQueryAsync();
    }
}
