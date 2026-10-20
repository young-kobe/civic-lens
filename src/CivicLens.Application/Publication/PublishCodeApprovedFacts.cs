using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class PublishCodeApprovedFacts(PublishRelease publish, PublishedRecordLoaders loaders)
{
    private static readonly ReviewActor Actor = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        var attempted = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var ready = await ListReadyAsync(cancellationToken);
            var key = KeyOf(ready);
            if (ready.IsEmpty || !attempted.Add(key)) return published;
            try
            {
                _ = await publish.ExecuteAsync(Actor, new([.. ready.Select(record => record.Reference)], key), cancellationToken);
            }
            catch (PublicationConflictException)
            {
                return published;
            }
            published += ready.Length;
        }
    }

    private async Task<ImmutableArray<ReadyToPublishRecord>> ListReadyAsync(CancellationToken cancellationToken)
    {
        var limit = PublishRelease.MaximumRecords;
        var actions = await loaders.For(PublishedRecordKind.Action).ListReadyAsync(limit, cancellationToken);
        var votes = await loaders.For(PublishedRecordKind.Vote).ListReadyAsync(limit, cancellationToken);
        return [.. actions.Ready.Concat(votes.Ready).Where(record => record.ApprovedByCode).Take(limit)];
    }

    // The key names each record and revision, so a retry after a crash replays the stored release.
    // A key seen twice in one tick means the batch is listed again after a replay, so the tick stops.
    private static string KeyOf(ImmutableArray<ReadyToPublishRecord> ready) =>
        "facts-" + ReviewValidation.HashPayload(ready
            .Select(record => $"{record.Reference}@{record.RevisionNumber}").Order(StringComparer.Ordinal).ToArray());
}
