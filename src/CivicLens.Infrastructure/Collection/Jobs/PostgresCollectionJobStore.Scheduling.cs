using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    public Task SynchronizeAsync(CollectionConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var revision = CollectionConfigurationRevision.Create(configuration);
        var snapshot = revision.Configuration;
        return TransactionAsync(async (db, now) =>
        {
            await new JobRowWriter(db, Ticks(now)).StoreAsync(revision, cancellationToken);
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
                        IntervalSeconds = intervalSeconds,
                        NextDueUtcTicks = Ticks(source.Enabled ? now + source.InitialDueOffset() : now.AddSeconds(intervalSeconds)),
                        Enabled = source.Enabled
                    };
                    db.Add(row);
                    continue;
                }

                var wasEnabled = row.Enabled;
                var intervalChanged = row.IntervalSeconds != intervalSeconds;
                row.ConfigurationRevisionId = revision.Id;
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
            var jobWriter = new JobRowWriter(db, nowTicks);
            var revisions = await StoredJobDefinition.LoadRevisionsAsync(db,
                dueRows.Select(row => row.ConfigurationRevisionId), cancellationToken);
            jobWriter.MarkStored(revisions.Keys);
            var sourcesWithActiveRoot = await ReadSourcesWithActiveRootAsync(db,
                dueRows.Select(row => row.SourceId).ToArray(), cancellationToken);
            var configurationsByRevision = new Dictionary<string, CollectionConfiguration>(StringComparer.Ordinal);
            foreach (var schedule in dueRows)
            {
                if (!configurationsByRevision.TryGetValue(schedule.ConfigurationRevisionId, out var configuration))
                {
                    configuration = StoredJobDefinition.Find(revisions, schedule.ConfigurationRevisionId).Configuration;
                    configurationsByRevision.Add(schedule.ConfigurationRevisionId, configuration);
                }
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

                if (sourcesWithActiveRoot.Contains(schedule.SourceId))
                {
                    schedule.NextDueUtcTicks = Ticks(now.AddSeconds(schedule.IntervalSeconds));
                    continue;
                }

                var configuredSource = new ConfiguredCollectionSource(configuration, schedule.SourceId, currentDate);
                var definition = await CreateScheduledDefinitionAsync(db, configuredSource, source, now, cancellationToken);
                if (definition is null)
                {
                    schedule.NextDueUtcTicks = Ticks(now.AddSeconds(schedule.IntervalSeconds));
                    continue;
                }
                var idempotencyKey = $"scheduled-check/{schedule.SourceId}/{schedule.NextDueUtcTicks}";
                _ = await jobWriter.AddJobAsync(idempotencyKey, definition, cancellationToken);
                schedule.NextDueUtcTicks = Ticks(now.AddSeconds(schedule.IntervalSeconds));
            }

            return dueRows.Length;
        }, cancellationToken);
    }

    private static async Task<CollectionJobDefinition?> CreateScheduledDefinitionAsync(CollectionAttemptDbContext db,
        ConfiguredCollectionSource configured, WatchedSourceConfiguration source, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(now.UtcDateTime);
        switch (source.ApiParser)
        {
            case null:
                return configured.CreateJobDefinition(currentDate);
            case ApiParser.CongressBillList:
                var cursor = await ReadCursorAsync(db, source.Id, cancellationToken);
                var window = BillStreamWindow.Next(cursor, source.BackfillFrom!.Value, now);
                return window is null ? null : configured.CreateApiJobDefinition(
                    ApiResource.BillList, window.Value.RequestUrl(source.Url), currentDate);
            case ApiParser.CongressHouseVoteList:
                var first = HouseVoteSession.FirstOfSweep(source.BackfillFrom!.Value, currentDate, source.MinimumCongress!.Value);
                return first is null ? null : configured.CreateApiJobDefinition(
                    ApiResource.HouseVoteList, HouseVoteListUrl.FirstPage(source.Url, first.Value), currentDate);
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    private static async Task<HashSet<string>> ReadSourcesWithActiveRootAsync(CollectionAttemptDbContext db,
        string[] sourceIds, CancellationToken cancellationToken)
    {
        var active = await db.Set<JobRow>().AsNoTracking()
            .Where(row => row.IsRoot && sourceIds.Contains(row.SourceId) &&
                (row.State == CollectionJobState.Pending || row.State == CollectionJobState.Running ||
                 row.State == CollectionJobState.WaitingToRetry))
            .Select(row => row.SourceId).Distinct().ToListAsync(cancellationToken);
        return new HashSet<string>(active, StringComparer.Ordinal);
    }
}
