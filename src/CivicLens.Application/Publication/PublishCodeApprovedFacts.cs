using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class PublishCodeApprovedFacts(PublishRelease publish, IPublicationStore publications, PublishedRecordLoaders loaders,
    Action<Exception> reportFailure, Action<string> log)
{
    private static readonly ReviewActor Actor = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        var skipped = new HashSet<PublishedRecordReference>();
        while (true)
        {
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
            catch (WithdrawnFactException exception)
            {
                var record = UnpublishableRecord.Of(exception)!;
                skipped.Add(record);
                log($"{record} left out of the release: {exception.Message}");
                continue;
            }
            catch (ArgumentException exception) when (UnpublishableRecord.Of(exception) is { } record)
            {
                skipped.Add(record);
                reportFailure(new InvalidDataException($"{record} left out of the release: {exception.Message}", exception));
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
        var actions = await loaders.Actions.ListCodeApprovedReadyAsync(limit, excluded, cancellationToken);
        var votes = await loaders.Votes.ListCodeApprovedReadyAsync(limit, excluded, cancellationToken);
        return [.. actions.Concat(votes).Take(limit)];
    }

    // The key names each record and revision, so a retry after a crash replays the stored release.
    private static string KeyOf(ImmutableArray<ReadyToPublishRecord> ready) =>
        "facts-" + ReviewValidation.HashPayload(ready
            .Select(record => $"{record.Reference}@{record.RevisionNumber}").Order(StringComparer.Ordinal).ToArray());
}
