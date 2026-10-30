using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Publication;

public sealed class PublishApprovedRecords(PublishRelease publish, IPublicationStore publications, PublishedRecordLoaders loaders,
    ILogger<PublishApprovedRecords> logger, WorkerBeat beat)
{
    private static readonly ReviewActor Actor = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        var skipped = new HashSet<PublishedRecordReference>();
        while (true)
        {
            await beat.BeatIfDueAsync(cancellationToken);
            // After a rollback the active release is not the latest. The stage waits until the owner activates the latest release.
            var state = await publications.GetStateAsync(cancellationToken);
            if (state.Active?.ReleaseNumber != state.LatestReleaseNumber && state.LatestReleaseNumber > 0) return published;
            var ready = await ListReadyAsync(skipped, cancellationToken);
            if (ready.IsEmpty) return published;
            PublicationReleaseSummary summary;
            try
            {
                summary = await publish.ExecuteAsync(Actor, new([.. ready.Select(record => record.Reference)], KeyOf(ready)), cancellationToken);
            }
            catch (PublicationConflictException)
            {
                return published;
            }
            catch (Exception exception) when (exception is WithdrawnRecordException or MissingCorrectionNoteException)
            {
                var record = UnpublishableRecord.Of(exception)!;
                skipped.Add(record);
                logger.LogInformation("{Record} left out of the release: {Reason}", record, exception.Message);
                continue;
            }
            catch (ArgumentException exception) when (UnpublishableRecord.Of(exception) is { } record)
            {
                skipped.Add(record);
                logger.LogError(exception, "{Record} left out of the release.", record);
                continue;
            }
            // A release that is not new replayed a stored one, and the same batch would list again.
            if (summary.ReleaseNumber <= state.LatestReleaseNumber) return published;
            published += ready.Length;
        }
    }

    private async Task<ImmutableArray<ReadyToPublishRecord>> ListReadyAsync(HashSet<PublishedRecordReference> skipped,
        CancellationToken cancellationToken)
    {
        var limit = PublishRelease.MaximumRecords;
        string[] excluded = [.. skipped.Select(record => record.RecordId)];
        var reads = await Task.WhenAll(loaders.All.Select(loader => loader.ListReadyAsync(limit, excluded, cancellationToken)));
        return ShareLimitBetweenKinds([.. reads.Select(read => read.Ready)], limit);
    }

    // Each kind gets an equal share of the limit. A kind with fewer ready records gives its unused share to the others.
    internal static ImmutableArray<ReadyToPublishRecord> ShareLimitBetweenKinds(IReadOnlyList<ReadyToPublishRecord>[] kinds, int limit)
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
