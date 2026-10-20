using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class PublishRelease
{
    public const int MaximumRecords = 64;

    private readonly IPublicationStore publications;
    private readonly IReleaseDirectory releases;
    private readonly IReleaseRenderer renderer;
    private readonly PublishedRecordLoaders loaders;
    private readonly ReleasePages releasePages;
    private readonly PublicationCatalog catalog;
    private readonly ReleaseIndexPages indexPages;
    private readonly ReleaseDataFiles dataFiles;
    private readonly ReleaseOfficialPages officialPages;
    private readonly PublishedRecordRules rules;
    private readonly TimeProvider clock;

    public PublishRelease(IPublicationStore publications, IReleaseDirectory releases, IReleaseRenderer renderer,
        PublishedRecordLoaders loaders, PublicationCatalog catalog, PublicationOfficials officials, TimeProvider clock)
    {
        this.publications = publications;
        this.releases = releases;
        this.renderer = renderer;
        this.loaders = loaders;
        releasePages = new ReleasePages(releases, loaders);
        this.catalog = catalog;
        indexPages = new ReleaseIndexPages(releases, renderer);
        dataFiles = new ReleaseDataFiles(releases, loaders.Votes, officials);
        officialPages = new ReleaseOfficialPages(new ReleaseOfficialVotes(releases, loaders.Votes), officials, renderer);
        this.clock = clock;
        rules = new PublishedRecordRules(catalog);
    }

    public async Task<PublicationReleaseSummary> ExecuteAsync(ReviewActor actor, PublishReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor, allowCode: IsOnlyFactRecords(request));
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
        var build = new PublicationBuild(previous, now, rules, renderer);
        var additions = await LoadAdditionsAsync(references, build, cancellationToken);
        var pages = await releasePages.RenderAsync(build.WithAdded(additions), additions, cancellationToken);
        var requested = references.ToHashSet();
        var carried = previous.Entries.Where(entry => !requested.Contains(new(entry.Kind, entry.RecordId))).ToList();
        if (carried.Count + additions.Count > PublicationProtocol.MaximumRecordsPerRelease)
            throw new ArgumentException("The release would exceed the maximum record count.", nameof(request));

        var number = state.LatestReleaseNumber + 1;
        var (directory, entries) = await StageAsync(number, now, previous, carried, additions, pages, cancellationToken);
        var commit = new PublicationCommit(number, state.Active?.ReleaseNumber, directory,
            [.. entries.Select(PublishedRevisionBinding.Of)],
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

    // Judgment records need a person, so the code subject may publish only the records that code approves.
    private static bool IsOnlyFactRecords(PublishReleaseRequest request) =>
        request is { Records.IsDefaultOrEmpty: false } &&
        request.Records.All(record => record.Kind is PublishedRecordKind.Action or PublishedRecordKind.Vote);

    private ImmutableArray<PublishedRecordReference> ValidateRequest(PublishReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var records = request.Records;
        if (records.IsDefaultOrEmpty || records.Length > MaximumRecords)
            throw new ArgumentException($"Select 1 to {MaximumRecords} records to publish.", nameof(request));
        foreach (var record in records) loaders.For(record.Kind).ValidateId(record.RecordId);
        if (records.ToHashSet().Count != records.Length)
            throw new ArgumentException("Selected records must be distinct.", nameof(request));
        return records;
    }

    private async Task<List<PendingRecord>> LoadAdditionsAsync(ImmutableArray<PublishedRecordReference> references,
        PublicationBuild build, CancellationToken cancellationToken)
    {
        var additions = new List<PendingRecord>(references.Length);
        foreach (var loader in loaders.All)
            additions.AddRange(await loader.LoadAsync(IdsOf(references, loader.Kind), build.WithAdded(additions), cancellationToken));
        return additions;
    }

    private static ImmutableArray<string> IdsOf(ImmutableArray<PublishedRecordReference> references, PublishedRecordKind kind) =>
        [.. references.Where(reference => reference.Kind == kind).Select(reference => reference.RecordId)];

    private async Task<(string Directory, List<PublishedRecordEntry> Entries)> StageAsync(int number, DateTimeOffset now,
        PreviousRelease previous, List<PublishedRecordEntry> carried, List<PendingRecord> additions,
        IReadOnlyDictionary<string, string> pages, CancellationToken cancellationToken)
    {
        List<PublishedRecordEntry> entries = [.. carried, .. additions.Select(addition => addition.Entry)];
        var manifest = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = number,
            PublishedAtUtc = now,
            Records = [.. entries.OrderByDescending(entry => entry.FirstPublishedAtUtc)
                .ThenBy(entry => entry.RecordId, StringComparer.Ordinal)]
        };
        manifest.Validate();
        var manifestJson = JsonSerializer.SerializeToUtf8Bytes(manifest, PublicationProtocol.JsonOptions);
        if (manifestJson.Length > PublicationProtocol.MaximumManifestBytes)
            throw new ArgumentException("The release manifest would exceed the manifest size limit.", nameof(additions));

        await using var staging = await releases.BeginAsync(cancellationToken);
        foreach (var entry in carried)
        {
            await staging.LinkFileAsync(previous.DirectoryName!, PublicationProtocol.RecordDataPath(entry.RecordId), cancellationToken);
            if (pages.TryGetValue(entry.RecordId, out var page))
                await staging.WriteFileAsync(PublicationProtocol.RecordPagePath(entry.RecordId), Encoding.UTF8.GetBytes(page), cancellationToken);
            else
                await staging.LinkFileAsync(previous.DirectoryName!, PublicationProtocol.RecordPagePath(entry.RecordId), cancellationToken);
        }
        foreach (var addition in additions)
        {
            await staging.WriteFileAsync(PublicationProtocol.RecordDataPath(addition.RecordId), addition.Json, cancellationToken);
            await staging.WriteFileAsync(PublicationProtocol.RecordPagePath(addition.RecordId),
                Encoding.UTF8.GetBytes(pages[addition.RecordId]), cancellationToken);
        }

        foreach (var asset in renderer.Assets)
            await staging.WriteFileAsync(asset.RelativePath, asset.Content, cancellationToken);
        await staging.WriteFileAsync(PublicationProtocol.ManifestGzipPath,
            Compress(manifestJson, stream => new GZipStream(stream, CompressionLevel.Optimal)), cancellationToken);
        await staging.WriteFileAsync(PublicationProtocol.ManifestBrotliPath,
            Compress(manifestJson, stream => new BrotliStream(stream, CompressionLevel.Optimal)), cancellationToken);
        var facets = ReleaseFacets.From(manifest, catalog);
        await indexPages.WriteAsync(staging, manifest, facets, additions, previous, cancellationToken);
        var officials = await dataFiles.WriteAsync(staging, manifest, facets, additions, previous, cancellationToken);
        await officialPages.WriteAsync(staging, manifest, officials, additions, previous, cancellationToken);
        await staging.WriteFileAsync(PublicationProtocol.ManifestPath, manifestJson, cancellationToken);
        return (await staging.CompleteAsync(number, cancellationToken), entries);
    }

    private static byte[] Compress(byte[] content, Func<Stream, Stream> compressor)
    {
        using var output = new MemoryStream();
        using (var compressed = compressor(output)) compressed.Write(content);
        return output.ToArray();
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
