using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Publication;

public sealed class PublishApprovedRecords(PublishRelease publish, IPublicationStore publications, PublishedRecordLoaders loaders,
    ILogger<PublishApprovedRecords> logger, WorkerBeat beat, TimeProvider clock, IIntervalWakeup wakeup)
{
    /// <summary>The least time between two releases of the stage. The database clock measures it from the stored time of the newest
    /// release. A failed or conflicted tick starts the same gap on the clock of this process.</summary>
    public static readonly TimeSpan ReleaseGap = TimeSpan.FromMinutes(10);

    private static readonly ReviewActor Actor = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    private long? failedAt;

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (await IsInsideGapAsync(cancellationToken)) return 0;
        try
        {
            return await PublishOneReleaseAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failedAt = clock.GetTimestamp();
            throw;
        }
    }

    private async Task<bool> IsInsideGapAsync(CancellationToken cancellationToken)
    {
        if (failedAt is { } failure && clock.GetElapsedTime(failure) < ReleaseGap) return true;
        return await publications.ReadAgeOfNewestReleaseAsync(cancellationToken) < ReleaseGap;
    }

    private async Task<int> PublishOneReleaseAsync(CancellationToken cancellationToken)
    {
        var skipped = new HashSet<string>();
        ImmutableArray<ReadyToPublishRecord> fitted = default;
        while (true)
        {
            await beat.BeatIfDueAsync(cancellationToken);
            // After a rollback the active release is not the latest. The stage waits until the owner activates the latest release.
            var state = await publications.GetStateAsync(cancellationToken);
            if (state.Active?.ReleaseNumber != state.LatestReleaseNumber && state.LatestReleaseNumber > 0) return 0;
            var ready = fitted.IsDefault ? await ListReadyAsync(skipped, cancellationToken) : [.. fitted.Where(record => !skipped.Contains(record.RecordId))];
            if (ready.IsEmpty)
            {
                if (wakeup.IntervalElapsed) await PublishStatusChangesAsync(state, cancellationToken);
                return 0;
            }
            PublicationReleaseSummary summary;
            try
            {
                summary = await publish.ExecuteAsync(Actor, new([.. ready.Select(record => record.Reference)], KeyOf(ready)), cancellationToken);
            }
            catch (PublicationConflictException)
            {
                failedAt = clock.GetTimestamp();
                return 0;
            }
            catch (ReleaseFullException exception) when (fitted.IsDefault)
            {
                fitted = LeaveOutWhatDoesNotFit(ready, exception);
                continue;
            }
            catch (Exception exception) when (exception is WithdrawnRecordException or MissingCorrectionNoteException or CorrectionLimitException)
            {
                var record = UnpublishableRecord.Of(exception)!;
                skipped.Add(record.RecordId);
                logger.LogInformation("{Record} left out of the release: {Reason}", record, exception.Message);
                continue;
            }
            catch (ArgumentException exception) when (UnpublishableRecord.Of(exception) is { } record)
            {
                skipped.Add(record.RecordId);
                logger.LogError(exception, "{Record} left out of the release.", record);
                continue;
            }
            // A release that is not new replayed a stored one, and the same batch would list again.
            return summary.ReleaseNumber <= state.LatestReleaseNumber ? 0 : ready.Length;
        }
    }

    // A bill can move while no record is ready. One release then shows the new status. The key names the release it builds on.
    private async Task PublishStatusChangesAsync(PublicationState state, CancellationToken cancellationToken)
    {
        if (!await publish.HasBillStatusChangeAsync(cancellationToken)) return;
        try
        {
            _ = await publish.ExecuteAsync(Actor, new([], $"status-after-{state.LatestReleaseNumber}") { StatusOnly = true }, cancellationToken);
        }
        catch (PublicationConflictException)
        {
            failedAt = clock.GetTimestamp();
        }
    }

    // A replacement or a return adds no entry, so it always fits. New records share the free slots, as kinds share a batch.
    private ImmutableArray<ReadyToPublishRecord> LeaveOutWhatDoesNotFit(ImmutableArray<ReadyToPublishRecord> ready, ReleaseFullException full)
    {
        var added = full.NewRecords.ToHashSet();
        var fitting = ShareLimitBetweenKinds([.. ready.Where(record => added.Contains(record.Reference)).GroupBy(record => record.Kind)
            .Select(kind => (IReadOnlyList<ReadyToPublishRecord>)[.. kind])], full.FreeSlots);
        logger.LogError("The release holds {Cap} records. {Waiting} new records of this batch wait for room. Raise the cap.",
            PublicationProtocol.MaximumRecordsPerRelease, added.Count - fitting.Length);
        return [.. ready.Where(record => !added.Contains(record.Reference)), .. fitting];
    }

    private async Task<ImmutableArray<ReadyToPublishRecord>> ListReadyAsync(HashSet<string> skipped, CancellationToken cancellationToken)
    {
        var limit = PublishRelease.MaximumRecords;
        var reads = await Task.WhenAll(loaders.All.Select(loader => ListKindAsync(loader, skipped, limit, cancellationToken)));
        return ShareLimitBetweenKinds(reads, limit);
    }

    // Rows that are not ready can fill a read. The read repeats without them until the kind has enough ready records or no row is left.
    // A not ready record may become ready after the next release, so only this read leaves it out.
    private static async Task<IReadOnlyList<ReadyToPublishRecord>> ListKindAsync(PublishedRecordLoader loader, HashSet<string> skipped, int limit,
        CancellationToken cancellationToken)
    {
        var excluded = new HashSet<string>(skipped);
        var ready = new List<ReadyToPublishRecord>();
        while (true)
        {
            var read = await loader.ListReadyAsync(limit, [.. excluded], cancellationToken);
            ready.AddRange(read.Ready);
            if (!read.HasMore || ready.Count >= limit) return ready;
            excluded.UnionWith(read.Ready.Select(record => record.RecordId));
            excluded.UnionWith(read.NotReadyIds);
        }
    }

    // Each kind gets an equal share of the limit. A kind with fewer ready records gives its unused share to the others.
    private static ImmutableArray<ReadyToPublishRecord> ShareLimitBetweenKinds(IReadOnlyList<ReadyToPublishRecord>[] kinds, int limit)
    {
        var counts = new int[kinds.Length];
        var remaining = limit;
        var smallestFirst = Enumerable.Range(0, kinds.Length).OrderBy(kind => kinds[kind].Count).ToArray();
        for (var position = 0; position < smallestFirst.Length; position++)
        {
            var kind = smallestFirst[position];
            counts[kind] = Math.Min(kinds[kind].Count, remaining / (smallestFirst.Length - position));
            remaining -= counts[kind];
        }
        return [.. kinds.SelectMany((ready, kind) => ready.Take(counts[kind]))];
    }

    // The key names each record and revision, so a retry after a crash replays the stored release.
    private static string KeyOf(ImmutableArray<ReadyToPublishRecord> ready) =>
        "approved-" + ReviewValidation.HashPayload(ready
            .Select(record => $"{record.Reference}@{record.RevisionNumber}").Order(StringComparer.Ordinal).ToArray());
}
