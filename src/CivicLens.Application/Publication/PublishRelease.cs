using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class PublishRelease
{
    public const int MaximumRecords = 64;

    private readonly IPublicationStore publications;
    private readonly IReleaseDirectory releases;
    private readonly IReleaseRenderer renderer;
    private readonly DocumentChangeRecordLoader documentChanges;
    private readonly StatementRecordLoader statements;
    private readonly TimeProvider clock;

    public PublishRelease(IPublicationStore publications, IReleaseDirectory releases, IReleaseRenderer renderer,
        IDocumentChangeReviewStore documentChangeReviews, IStatementReviewStore statementReviews, PublicationCatalog catalog,
        TimeProvider clock)
    {
        this.publications = publications;
        this.releases = releases;
        this.renderer = renderer;
        this.clock = clock;
        var builder = new PublishedRecordBuilder(catalog);
        documentChanges = new(documentChangeReviews, renderer, builder);
        statements = new(statementReviews, renderer, new(builder));
    }

    public async Task<PublicationReleaseSummary> ExecuteAsync(ReviewActor actor, PublishReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var references = ValidateRequest(request);
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
        var additions = await LoadAdditionsAsync(references, previous, now, cancellationToken);
        var requested = references.ToHashSet();
        var carried = previous.Entries.Where(entry => !requested.Contains(new(entry.Kind, entry.RecordId))).ToList();
        if (carried.Count + additions.Count > PublicationProtocol.MaximumRecordsPerRelease)
            throw new ArgumentException("The release would exceed the maximum record count.", nameof(request));

        var number = state.LatestReleaseNumber + 1;
        var (directory, entries) = await StageAsync(number, now, previous, carried, additions, cancellationToken);
        var commit = new PublicationCommit(number, state.Active?.ReleaseNumber, directory, now,
            [.. entries.Select(entry => new PublishedRevisionBinding(entry.Kind, entry.RecordId, entry.RevisionNumber))],
            [.. additions.Select(addition => addition.Expectation)]);
        var summary = await CommitAsync(actor, commit, request.IdempotencyKey, payloadHash, cancellationToken);
        await ServeActiveReleaseAsync(cancellationToken);
        return summary;
    }

    // Reads the active release again under the publication lock, so an older command cannot serve a stale release.
    private async Task ServeActiveReleaseAsync(CancellationToken cancellationToken)
    {
        _ = await publications.ServeActiveAsync(releases.ActivateAsync, cancellationToken)
            ?? throw new InvalidDataException("A release was committed but none is active.");
    }

    private static ImmutableArray<PublishedRecordReference> ValidateRequest(PublishReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var records = request.Records;
        if (records.IsDefaultOrEmpty || records.Length > MaximumRecords)
            throw new ArgumentException($"Select 1 to {MaximumRecords} records to publish.", nameof(request));
        foreach (var record in records) ValidateId(record);
        if (records.ToHashSet().Count != records.Length)
            throw new ArgumentException("Selected records must be distinct.", nameof(request));
        return records;
    }

    private static void ValidateId(PublishedRecordReference record)
    {
        switch (record.Kind)
        {
            case PublishedRecordKind.DocumentChange:
                ReviewValidation.ValidateDraftId(record.RecordId);
                break;
            case PublishedRecordKind.Statement:
                ReviewValidation.ValidateCandidateId(record.RecordId);
                break;
            default:
                throw new ArgumentException("Record kind is unknown.", nameof(record));
        }
    }

    private async Task<List<PendingRecord>> LoadAdditionsAsync(ImmutableArray<PublishedRecordReference> references,
        PreviousRelease previous, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var additions = await documentChanges.LoadAsync(IdsOf(references, PublishedRecordKind.DocumentChange), previous, now, cancellationToken);
        additions.AddRange(await statements.LoadAsync(IdsOf(references, PublishedRecordKind.Statement), previous, now, cancellationToken));
        return additions;
    }

    private static ImmutableArray<string> IdsOf(ImmutableArray<PublishedRecordReference> references, PublishedRecordKind kind) =>
        [.. references.Where(reference => reference.Kind == kind).Select(reference => reference.RecordId)];

    private async Task<(string Directory, List<PublishedRecordEntry> Entries)> StageAsync(int number, DateTimeOffset now,
        PreviousRelease previous, List<PublishedRecordEntry> carried, List<PendingRecord> additions,
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
            await staging.WriteFileAsync(PublicationProtocol.RecordDataPath(addition.RecordId), addition.Json, cancellationToken);
            await staging.WriteFileAsync(PublicationProtocol.RecordPagePath(addition.RecordId),
                Encoding.UTF8.GetBytes(addition.Page), cancellationToken);
            entries.Add(addition.Entry);
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
