using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    public Task SynchronizeAsync(CollectionConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var revision = CollectionConfigurationRevision.Create(configuration);
        var snapshot = revision.ReadConfiguration();
        return TransactionAsync(async (db, now) =>
        {
            var existingRows = await db.Set<CollectionScheduleRow>().ToListAsync(cancellationToken);
            var rowsBySource = existingRows.ToDictionary(row => row.SourceId, StringComparer.Ordinal);
            var configuredSourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in snapshot.Sources)
            {
                configuredSourceIds.Add(source.Id);
                var intervalSeconds = source.ResolveCheckIntervalSeconds();
                if (!rowsBySource.TryGetValue(source.Id, out var row))
                {
                    row = new CollectionScheduleRow
                    {
                        SourceId = source.Id,
                        ConfigurationRevisionId = revision.Id,
                        ConfigurationJson = revision.Json,
                        IntervalSeconds = intervalSeconds,
                        NextDueUtcTicks = source.Enabled ? Ticks(now) : Ticks(now.AddSeconds(intervalSeconds)),
                        Enabled = source.Enabled
                    };
                    db.Add(row);
                    continue;
                }

                var wasEnabled = row.Enabled;
                var intervalChanged = row.IntervalSeconds != intervalSeconds;
                row.ConfigurationRevisionId = revision.Id;
                row.ConfigurationJson = revision.Json;
                row.IntervalSeconds = intervalSeconds;
                row.Enabled = source.Enabled;
                if (source.Enabled && !wasEnabled)
                    row.NextDueUtcTicks = Ticks(now);
                else if (source.Enabled && intervalChanged)
                    row.NextDueUtcTicks = Ticks(now.AddSeconds(intervalSeconds));
            }

            foreach (var row in existingRows)
                if (!configuredSourceIds.Contains(row.SourceId)) row.Enabled = false;
            return true;
        }, cancellationToken);
    }

    public async Task<int> AdvanceDueAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return await TransactionAsync(async (db, now) =>
        {
            var nowTicks = Ticks(now);
            var dueRows = await db.Set<CollectionScheduleRow>()
                .Where(row => row.Enabled && row.NextDueUtcTicks <= nowTicks)
                .OrderBy(row => row.NextDueUtcTicks).ThenBy(row => row.SourceId)
                .Take(limit).ToArrayAsync(cancellationToken);
            var currentDate = DateOnly.FromDateTime(now.UtcDateTime);
            var configurationsByRevision = new Dictionary<string, (string Json, CollectionConfiguration Configuration)>(StringComparer.Ordinal);
            foreach (var schedule in dueRows)
            {
                if (!configurationsByRevision.TryGetValue(schedule.ConfigurationRevisionId, out var cached))
                {
                    var revision = new CollectionConfigurationRevision(schedule.ConfigurationRevisionId,
                        schedule.ConfigurationJson);
                    cached = (schedule.ConfigurationJson, revision.ReadConfiguration());
                    configurationsByRevision.Add(schedule.ConfigurationRevisionId, cached);
                }
                if (cached.Json != schedule.ConfigurationJson)
                    throw new InvalidDataException("A schedule configuration revision has conflicting retained JSON.");
                var configuration = cached.Configuration;
                var source = configuration.Sources.SingleOrDefault(candidate => candidate.Id == schedule.SourceId);
                if (source is null || !source.Enabled)
                {
                    schedule.Enabled = false;
                    continue;
                }

                if (source.ResolveCheckIntervalSeconds() != schedule.IntervalSeconds)
                    throw new InvalidDataException("Schedule interval does not match its retained configuration.");

                // Scheduled checks outside configured coverage consume one cadence slot without creating a job.
                if (configuration.GetCoveredPersonIds(schedule.SourceId, currentDate).Length == 0)
                {
                    schedule.NextDueUtcTicks = Ticks(now.AddSeconds(schedule.IntervalSeconds));
                    continue;
                }

                if (await HasActiveRootJobAsync(db, schedule.SourceId, source.Url, cancellationToken))
                {
                    schedule.NextDueUtcTicks = Ticks(now.AddSeconds(schedule.IntervalSeconds));
                    continue;
                }

                var configuredSource = new ConfiguredCollectionSource(configuration, schedule.SourceId, currentDate);
                var definition = configuredSource.CreateJobDefinition(currentDate);
                var idempotencyKey = $"scheduled-check/{schedule.SourceId}/{schedule.NextDueUtcTicks}";
                var row = new JobRow
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    IdempotencyKey = idempotencyKey,
                    DefinitionJson = Write(definition),
                    State = CollectionJobState.Pending,
                    CreatedAt = nowTicks
                };
                db.Add(row);
                schedule.NextDueUtcTicks = Ticks(now.AddSeconds(schedule.IntervalSeconds));
            }

            return dueRows.Length;
        }, cancellationToken);
    }

    private static async Task<bool> HasActiveRootJobAsync(CollectionAttemptDbContext db, string sourceId,
        string requestedUrl, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM collection_jobs
                 WHERE state IN ('Pending','Running','WaitingToRetry')
                   AND definition_json::jsonb ->> 'sourceId' = @source_id
                   AND definition_json::jsonb ->> 'url' = @requested_url)
            """;
        command.Parameters.Add(new NpgsqlParameter("source_id", sourceId));
        command.Parameters.Add(new NpgsqlParameter("requested_url", requestedUrl));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Postgres did not return active-job state."));
    }
}
