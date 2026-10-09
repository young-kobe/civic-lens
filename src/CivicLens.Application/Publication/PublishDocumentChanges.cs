using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class PublishDocumentChanges(IPublicationStore publications, IReleaseDirectory releases,
    IReleaseRenderer renderer, IDocumentChangeReviewStore reviews, PublicationCatalog catalog, TimeProvider clock)
{
    private readonly PublishedRecordBuilder builder = new(catalog);

    public async Task<PublicationReleaseSummary> ExecuteAsync(ReviewActor actor, PublishDocumentChangesRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var draftIds = ValidateRequest(request);
        var payloadHash = ReviewValidation.HashPayload(request);
        var replay = await publications.FindReplayAsync(actor.Subject, request.IdempotencyKey, payloadHash, cancellationToken);
        if (replay is not null)
        {
            await ServeActiveReleaseAsync(cancellationToken);
            return replay;
        }

        var now = clock.GetUtcNow();
        var state = await publications.GetStateAsync(cancellationToken);
        var previous = await PreviousRelease.LoadAsync(releases, state.Active, cancellationToken);
        var additions = await LoadAdditionsAsync(draftIds, previous, cancellationToken);
        var carried = previous.Entries.Where(entry => !draftIds.Contains(entry.RecordId)).ToList();
        if (carried.Count + additions.Count > PublicationProtocol.MaximumRecordsPerRelease)
            throw new ArgumentException("The release would exceed the maximum record count.", nameof(request));

        var number = state.LatestReleaseNumber + 1;
        var (directory, entries) = await StageAsync(number, now, previous, carried, additions, cancellationToken);
        var commit = new PublicationCommit(number, directory, now,
            [.. entries.Select(entry => new PublishedRevisionBinding(entry.RecordId, entry.RevisionNumber))],
            [.. additions.Select(addition => new ReviewStateExpectation(addition.Review.DraftId,
                addition.Review.CurrentRevision.RevisionNumber, addition.Review.ReviewStateVersion))]);
        var summary = await CommitAsync(actor, commit, request.IdempotencyKey, payloadHash, cancellationToken);
        if (summary.DirectoryName == directory) await releases.ActivateAsync(directory, cancellationToken);
        else await ServeActiveReleaseAsync(cancellationToken);
        return summary;
    }

    private async Task ServeActiveReleaseAsync(CancellationToken cancellationToken)
    {
        var active = (await publications.GetStateAsync(cancellationToken)).Active
            ?? throw new InvalidDataException("A release was committed but none is active.");
        await releases.ActivateAsync(active.DirectoryName, cancellationToken);
    }

    private static ImmutableArray<string> ValidateRequest(PublishDocumentChangesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var ids = request.DraftIds;
        if (ids.IsDefaultOrEmpty || ids.Length > 64)
            throw new ArgumentException("Select 1 to 64 drafts to publish.", nameof(request));
        foreach (var id in ids) ReviewValidation.ValidateDraftId(id);
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("Selected drafts must be distinct.", nameof(request));
        return ids;
    }

    private async Task<List<PublishableDocumentChange>> LoadAdditionsAsync(ImmutableArray<string> draftIds,
        PreviousRelease previous, CancellationToken cancellationToken)
    {
        var found = await reviews.GetForPublicationAsync(draftIds, cancellationToken);
        var additions = new List<PublishableDocumentChange>(draftIds.Length);
        foreach (var draftId in draftIds)
        {
            var addition = found.GetValueOrDefault(draftId)
                ?? throw new ArgumentException($"Draft {draftId} does not exist.", nameof(draftIds));
            PublishedRecordBuilder.RequirePublishable(addition.Review);
            if (previous.Find(draftId)?.RevisionNumber == addition.Review.CurrentRevision.RevisionNumber)
                throw new ArgumentException($"Draft {draftId} is already published at its current revision.", nameof(draftIds));
            additions.Add(addition);
        }
        return additions;
    }

    private async Task<(string Directory, List<PublishedRecordEntry> Entries)> StageAsync(int number, DateTimeOffset now,
        PreviousRelease previous, List<PublishedRecordEntry> carried, List<PublishableDocumentChange> additions,
        CancellationToken cancellationToken)
    {
        await using var staging = await releases.BeginAsync(cancellationToken);
        var entries = new List<PublishedRecordEntry>();
        foreach (var entry in carried)
        {
            await staging.LinkFileAsync(previous.DirectoryName!, PublicationProtocol.RecordDataPath(entry.RecordId), cancellationToken);
            await staging.LinkFileAsync(previous.DirectoryName!, PublicationProtocol.RecordPagePath(entry.RecordId), cancellationToken);
            entries.Add(entry);
        }
        foreach (var addition in additions)
        {
            var record = builder.Build(addition, previous.Find(addition.Review.DraftId), now);
            var json = JsonSerializer.SerializeToUtf8Bytes(record, PublicationProtocol.JsonOptions);
            entries.Add(await WriteRecordAsync(staging, record, json, cancellationToken));
        }

        var manifest = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = number,
            PublishedAtUtc = now,
            Records = [.. entries.OrderByDescending(entry => entry.FirstPublishedAtUtc)
                .ThenBy(entry => entry.RecordId, StringComparer.Ordinal)]
        };
        manifest.Validate();
        foreach (var asset in renderer.Assets)
            await staging.WriteFileAsync(asset.RelativePath, asset.Content, cancellationToken);
        for (var page = 1; page <= PublicationProtocol.IndexPageCount(entries.Count); page++)
        {
            var index = await renderer.RenderIndexAsync(manifest, page, cancellationToken);
            await staging.WriteFileAsync(PublicationProtocol.IndexPagePath(page), Encoding.UTF8.GetBytes(index), cancellationToken);
        }
        await staging.WriteFileAsync(PublicationProtocol.ManifestPath,
            JsonSerializer.SerializeToUtf8Bytes(manifest, PublicationProtocol.JsonOptions), cancellationToken);
        return (await staging.CompleteAsync(number, cancellationToken), entries);
    }

    private async Task<PublishedRecordEntry> WriteRecordAsync(IReleaseStaging staging, PublishedDocumentChange record,
        byte[] json, CancellationToken cancellationToken)
    {
        await staging.WriteFileAsync(PublicationProtocol.RecordDataPath(record.RecordId), json, cancellationToken);
        var page = await renderer.RenderRecordAsync(record, cancellationToken);
        await staging.WriteFileAsync(PublicationProtocol.RecordPagePath(record.RecordId),
            Encoding.UTF8.GetBytes(page), cancellationToken);
        return new()
        {
            RecordId = record.RecordId,
            RevisionNumber = record.RevisionNumber,
            Headline = record.Headline,
            FirstPublishedAtUtc = record.FirstPublishedAtUtc
        };
    }

    private async Task<PublicationReleaseSummary> CommitAsync(ReviewActor actor, PublicationCommit commit,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        PublicationReleaseSummary summary;
        try
        {
            summary = await publications.CommitAsync(actor.Subject, commit, idempotencyKey, payloadHash, cancellationToken);
        }
        catch (Exception exception) when (exception is PublicationConflictException or ArgumentException)
        {
            await DiscardAsync(commit.DirectoryName);
            throw;
        }
        if (summary.DirectoryName != commit.DirectoryName) await DiscardAsync(commit.DirectoryName);
        return summary;
    }

    private async Task DiscardAsync(string directoryName)
    {
        try
        {
            await releases.DeleteAsync(directoryName, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
