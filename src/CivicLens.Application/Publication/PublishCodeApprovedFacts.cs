using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class PublishCodeApprovedFacts(PublishRelease publish, IPublicationStore publications, PublishedRecordLoaders loaders,
    Action<Exception> reportFailure)
{
    private static readonly ReviewActor Actor = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        // After a rollback the active release is not the latest. The tick waits until the owner activates the latest release.
        var state = await publications.GetStateAsync(cancellationToken);
        if (state.Active?.ReleaseNumber != state.LatestReleaseNumber && state.LatestReleaseNumber > 0) return 0;
        var latest = state.LatestReleaseNumber;
        var published = 0;
        var skipped = new HashSet<PublishedRecordReference>();
        while (true)
        {
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
            catch (ArgumentException exception) when (UnpublishableRecord.Of(exception) is { } record)
            {
                skipped.Add(record);
                reportFailure(new InvalidDataException($"{record} left out of the release: {exception.Message}", exception));
                continue;
            }
            // A release that is not new replayed a stored one, and the same batch would list again.
            if (summary.ReleaseNumber <= latest) return published;
            latest = summary.ReleaseNumber;
            published += ready.Length;
        }
    }

    private async Task<ImmutableArray<ReadyToPublishRecord>> ListReadyAsync(HashSet<PublishedRecordReference> skipped,
        CancellationToken cancellationToken)
    {
        var limit = PublishRelease.MaximumRecords;
        var actions = await loaders.Actions.ListCodeApprovedReadyAsync(limit, cancellationToken);
        var votes = await loaders.Votes.ListCodeApprovedReadyAsync(limit, cancellationToken);
        return [.. actions.Concat(votes).Where(record => !skipped.Contains(record.Reference)).Take(limit)];
    }

    // The key names each record and revision, so a retry after a crash replays the stored release.
    private static string KeyOf(ImmutableArray<ReadyToPublishRecord> ready) =>
        "facts-" + ReviewValidation.HashPayload(ready
            .Select(record => $"{record.Reference}@{record.RevisionNumber}").Order(StringComparer.Ordinal).ToArray());
}
