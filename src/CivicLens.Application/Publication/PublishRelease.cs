using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.People;
using CivicLens.Application.Review;
using CivicLens.Application.Roster;
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
    private readonly PublicationOfficials officials;
    private readonly TimeProvider clock;
    private readonly PruneReleases prune;

    public PublishRelease(IPublicationStore publications, IReleaseDirectory releases, IReleaseRenderer renderer,
        PublishedRecordLoaders loaders, PublicationCatalog catalog, PublicationOfficials officials, ReleasePhotos releasePhotos, TimeProvider clock, PruneReleases prune)
    {
        this.officials = officials;
        this.publications = publications;
        this.releases = releases;
        this.renderer = renderer;
        this.loaders = loaders;
        releasePages = new ReleasePages(releases, loaders);
        this.catalog = catalog;
        indexPages = new ReleaseIndexPages(releases, renderer);
        dataFiles = new ReleaseDataFiles(releases, loaders.Votes, officials, releasePhotos);
        officialPages = new ReleaseOfficialPages(new ReleaseOfficialVotes(releases, loaders.Votes), officials, renderer, catalog);
        this.clock = clock;
        this.prune = prune;
        rules = new PublishedRecordRules(catalog);
    }

    public async Task<PublicationReleaseSummary> ExecuteAsync(ReviewActor actor, PublishReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor, allowCode: true);
        var byCode = ReviewAuthor.IsCode(actor.Subject);
        var references = ValidateRequest(request);
        if (byCode && references.IsEmpty && !request.StatusOnly) throw new ArgumentException("The code subject publishes at least one record.", nameof(request));
        var withdrawalNotes = PublicationWithdrawals.ValidateNotes(request.Withdrawals, byCode);
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
        if (previous.DirectoryName is null && references.IsEmpty && request.Withdrawals.IsEmpty)
            throw new ArgumentException("A publish with no record needs an active release.");
        var withdrawn = ImmutableArray.CreateRange(withdrawalNotes.Keys);
        var listedWithdrawals = previous.Withdrawals.Select(withdrawal => new PublishedRecordReference(withdrawal.Kind, withdrawal.RecordId))
            .Concat(withdrawn.Where(reference => previous.Find(reference.Kind, reference.RecordId) is not null)).ToArray();
        var firstPublished = await publications.ReadFirstPublishedAsync([.. references.Concat(listedWithdrawals).Distinct()], cancellationToken);
        RequireFirstPublished(previous, references, listedWithdrawals, firstPublished);
        var corrections = await PublicationCorrections.LoadAsync(publications, releases, state.Active?.ReleaseNumber, previous, now, [.. references, .. withdrawn],
            cancellationToken);
        var newWithdrawals = PublicationWithdrawals.Create(previous, corrections, now, withdrawalNotes);
        var returned = ImmutableArray.CreateRange(references.Where(reference => previous.IsWithdrawn(reference.RecordId)));
        var roster = await officials.ReadRosterAsync(cancellationToken);
        var settings = await officials.ReadSettingsAsync(cancellationToken);
        var photos = await officials.ReadPhotosAsync(settings, cancellationToken);
        var billInputs = await ReadBillInputsAsync(state.Active?.ReleaseNumber, previous, references, cancellationToken);
        var statuses = ReleaseBillStatuses.KeepObservationTimes(previous, billInputs);
        var build = new PublicationBuild(previous, now, rules, renderer, catalog, corrections, roster, byCode, firstPublished)
            .WithRemoved(withdrawn).WithReturned(returned).WithBillStatuses(statuses);
        var additions = await LoadAdditionsAsync(references, build, cancellationToken);
        await WithdrawalCascade.RequireNoCitersAsync(releases, loaders, previous, withdrawn, additions, cancellationToken);
        var replaced = references.Concat(withdrawn).ToHashSet();
        var carried = previous.Entries.Where(entry => !replaced.Contains(new(entry.Kind, entry.RecordId))).ToList();
        if (request.RewriteEntries) carried = await ReleaseEntryRewrite.RebuildAsync(releases, carried, previous, cancellationToken);
        var released = build.WithAdded(additions);
        released = released.WithVoteBills([.. carried, .. additions.Select(addition => addition.Entry)], await loaders.Votes.BillActionRecordIdsAsync(
            [.. released.ReleaseRecords.Where(record => record.Kind == PublishedRecordKind.Vote)], cancellationToken));
        var pages = await releasePages.PlanAsync(released, additions, request.RewritePages, cancellationToken);
        var removed = previous.Entries.Where(entry => withdrawalNotes.ContainsKey(new(entry.Kind, entry.RecordId))).ToList();
        List<PublishedWithdrawal> withdrawals = [.. previous.Withdrawals.Where(withdrawal => !build.Returned.Any(record => record.RecordId == withdrawal.RecordId)), .. newWithdrawals];
        RequireRoom(previous, carried.Count + additions.Count + withdrawals.Count, additions);

        var billIds = ReleaseBillStatuses.ActionIdsOf(carried.Concat(additions.Select(addition => addition.Entry))).ToHashSet(StringComparer.Ordinal);
        var histories = billInputs.Where(input => billIds.Contains(input.RecordId) && input.History is not null).ToArray();
        await publications.PrepareBillHistoriesAsync([.. histories.Select(input => input.History!).Distinct()], cancellationToken);
        var correctionHistories = await ReleaseCorrectionHistories.PrepareAsync(publications, releases, state.Active?.ReleaseNumber,
            previous, carried, additions, cancellationToken);

        var historyChanged = billInputs.Any(input => billIds.Contains(input.RecordId) && input.HasHistoryChange);
        var number = state.LatestReleaseNumber + 1;
        var (directory, entries, names) = await StageAsync(number, now, previous, carried, additions, removed, withdrawals, pages, released.BillLines, statuses, roster, settings, photos, request.RewriteOfficials, request.RewriteEntries, request.RewritePages, historyChanged, cancellationToken);
        var commit = new PublicationCommit(number, state.Active?.ReleaseNumber, directory,
            [.. entries.Select(PublishedRevisionBinding.Of)],
            [.. additions.Select(addition => addition.Expectation)])
        {
            WithdrawnRecords = withdrawn,
            Withdrawals = [.. withdrawals],
            AddedEntries = [.. additions.Select(addition => addition.Entry)],
            RewrittenEntries = request.RewriteEntries ? [.. carried.Where(entry => entry.Kind == PublishedRecordKind.Action)] : [],
            VoteColumns = [.. additions.OfType<PendingRecord<PublishedVote>>().Select(addition => VoteStoredColumns.From(addition.Record))],
            Names = names,
            BillStatuses = ReleaseBillStatuses.RowsOf(ReleaseBillStatuses.ActionIdsOf(entries), statuses),
            VoteBills = [.. released.BillLines.VoteBills()],
            BillHistories = [.. histories.Select(input => new BillHistoryBinding(input.RecordId,
                input.History!.DetailJobId, input.History.ProjectionVersion))],
            CorrectionHistories = correctionHistories
        };
        var summary = await CommitAsync(actor, commit, request.IdempotencyKey, payloadHash, cancellationToken);
        await ServeActiveReleaseAsync(cancellationToken);
        await prune.ExecuteAsync(cancellationToken);
        return summary;
    }

    private static void RequireFirstPublished(PreviousRelease previous, ImmutableArray<PublishedRecordReference> references,
        PublishedRecordReference[] listedWithdrawals, IReadOnlyDictionary<PublishedRecordReference, DateTimeOffset> firstPublished)
    {
        var unrecorded = references.Where(reference => previous.Find(reference.Kind, reference.RecordId) is not null || previous.IsWithdrawn(reference.RecordId))
            .Concat(listedWithdrawals).FirstOrDefault(reference => !firstPublished.ContainsKey(reference));
        if (unrecorded is null) return;
        throw new FirstPublishedMissingException(
            $"The first publication time of {unrecorded} is not stored. Run releases first-published backfill, then publish again.");
    }

    private static void RequireRoom(PreviousRelease previous, int total, List<PendingRecord> additions)
    {
        if (total <= PublicationProtocol.MaximumRecordsPerRelease) return;
        var held = previous.Entries.Select(entry => entry.RecordId).Concat(previous.Withdrawals.Select(withdrawal => withdrawal.RecordId)).ToHashSet();
        ImmutableArray<PublishedRecordReference> added =
            [.. additions.Where(addition => !held.Contains(addition.RecordId)).Select(addition => new PublishedRecordReference(addition.Entry.Kind, addition.RecordId))];
        throw new ReleaseFullException(PublicationProtocol.MaximumRecordsPerRelease - (total - added.Length), added);
    }

    // The worker asks this when no record is ready. Reading the previous files and one bill input query is much cheaper than staging a release.
    public async Task<bool> HasBillInputChangeAsync(CancellationToken cancellationToken = default)
    {
        var state = await publications.GetStateAsync(cancellationToken);
        var previous = await PreviousRelease.LoadAsync(releases, state.Active, cancellationToken);
        if (previous.DirectoryName is null) return false;
        var inputs = await ReadBillInputsAsync(state.Active?.ReleaseNumber, previous, [], cancellationToken);
        var statuses = ReleaseBillStatuses.KeepObservationTimes(previous, inputs);
        return ReleaseBillStatuses.ChangedActionIds(ReleaseBillStatuses.ActionIdsOf(previous.Entries), previous, statuses).Any() ||
            inputs.Any(input => input.HasHistoryChange);
    }

    private Task<IReadOnlyList<BillPublicationInput>> ReadBillInputsAsync(int? previousReleaseNumber, PreviousRelease previous,
        ImmutableArray<PublishedRecordReference> references, CancellationToken cancellationToken) =>
        publications.ReadBillInputsAsync(previousReleaseNumber,
            [.. ReleaseBillStatuses.ActionIdsOf(previous.Entries).Concat(references.Where(record => record.Kind == PublishedRecordKind.Action)
                .Select(record => record.RecordId)).Distinct(StringComparer.Ordinal)], cancellationToken);

    // Reads the active release again under the publication lock, so an older command cannot serve a stale release.
    private async Task ServeActiveReleaseAsync(CancellationToken cancellationToken)
    {
        _ = await publications.ServeActiveAsync(releases.ActivateAsync, cancellationToken)
            ?? throw new InvalidDataException("A release was committed but none is active.");
    }

    private ImmutableArray<PublishedRecordReference> ValidateRequest(PublishReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var records = request.Records;
        if (records.IsDefault || request.Withdrawals.IsDefault)
            throw new ArgumentException("Records and withdrawals must not be default.", nameof(request));
        if (request.Withdrawals.Any(withdrawal => withdrawal is null))
            throw new ArgumentException("Withdrawals must not hold null.", nameof(request));
        var named = records.Concat(request.Withdrawals.Select(withdrawal => withdrawal.Reference)).ToArray();
        if (named.Length > MaximumRecords)
            throw new ArgumentException($"Select at most {MaximumRecords} records to publish or withdraw.", nameof(request));
        foreach (var record in named) loaders.For(record.Kind).ValidateId(record.RecordId);
        if (named.ToHashSet().Count != named.Length)
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

    private async Task<(string Directory, List<PublishedRecordEntry> Entries, ReleaseNames Names)> StageAsync(int number, DateTimeOffset now,
        PreviousRelease previous, List<PublishedRecordEntry> carried, List<PendingRecord> additions,
        List<PublishedRecordEntry> removed, List<PublishedWithdrawal> withdrawals, IReadOnlyDictionary<string, StagedPage> pages, ReleaseBillLines billLines, IReadOnlyDictionary<string, ActionBillStatus> statuses, IReadOnlyList<RosterMember> roster,
        IReadOnlyDictionary<string, PersonSetting> settings, IReadOnlyList<OfficialPhoto> photos, bool rewriteOfficials, bool rewriteEntries, bool rewritePages, bool historyChanged, CancellationToken cancellationToken)
    {
        List<PublishedRecordEntry> entries = [.. carried, .. additions.Select(addition => addition.Entry)];
        var manifest = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = number,
            PublishedAtUtc = now,
            Records = [.. ReleaseRecordOrder.NewestFirst(entries, ReleaseRecordOrder.Day, entry => entry.FirstPublishedAtUtc, entry => entry.RecordId)],
            Withdrawals = [.. ReleaseRecordOrder.WithdrawalsNewestFirst(withdrawals)]
        };
        manifest.Validate();
        var manifestJson = JsonSerializer.SerializeToUtf8Bytes(manifest, PublicationProtocol.JsonOptions);
        if (manifestJson.Length > PublicationProtocol.MaximumManifestBytes)
            throw new ArgumentException("The release manifest would exceed the manifest size limit.", nameof(additions));

        await using var staging = new ReleaseSite(await releases.BeginAsync(cancellationToken), renderer);
        foreach (var entry in carried)
        {
            await staging.LinkFileAsync(previous.DirectoryName!, PublicationProtocol.RecordDataPath(entry.RecordId), cancellationToken);
            if (pages.TryGetValue(entry.RecordId, out var page))
                await staging.WriteFileAsync(PublicationProtocol.RecordPagePath(entry.RecordId), Encoding.UTF8.GetBytes(await page(cancellationToken)), cancellationToken);
            else
                await staging.LinkFileAsync(previous.DirectoryName!, PublicationProtocol.RecordPagePath(entry.RecordId), cancellationToken);
        }
        foreach (var addition in additions)
        {
            await staging.WriteFileAsync(PublicationProtocol.RecordDataPath(addition.RecordId), addition.Json, cancellationToken);
            await staging.WriteFileAsync(PublicationProtocol.RecordPagePath(addition.RecordId),
                Encoding.UTF8.GetBytes(await pages[addition.RecordId](cancellationToken)), cancellationToken);
        }

        await WriteWithdrawalPagesAsync(staging, withdrawals, cancellationToken);
        foreach (var asset in renderer.Assets)
            await staging.WriteFileAsync(asset.RelativePath, asset.Content, cancellationToken);
        await staging.WriteFileAsync(PublicationProtocol.ManifestGzipPath,
            Compress(manifestJson, stream => new GZipStream(stream, CompressionLevel.Optimal)), cancellationToken);
        await staging.WriteFileAsync(PublicationProtocol.ManifestBrotliPath,
            Compress(manifestJson, stream => new BrotliStream(stream, CompressionLevel.Optimal)), cancellationToken);
        var facets = ReleaseFacets.From(manifest, catalog);
        await indexPages.WriteAsync(staging, manifest, facets, billLines, cancellationToken);
        await ReleaseBillStatuses.WriteAsync(staging, ReleaseBillStatuses.RowsOf(ReleaseBillStatuses.ActionIdsOf(manifest.Records), statuses), cancellationToken);
        var officials = await dataFiles.WriteAsync(staging, manifest, facets, additions, previous, roster, settings, photos, cancellationToken);
        if (!historyChanged && additions.Count == 0 && removed.Count == 0 && !rewriteOfficials && !rewriteEntries && !rewritePages && !ReleaseOfficialPages.ChangesProfiles(previous, officials) &&
            !ReleaseBillStatuses.ChangedActionIds(ReleaseBillStatuses.ActionIdsOf(manifest.Records), previous, statuses).Any())
            throw new ArgumentException("Nothing to publish: no record, official, or bill input changed.");
        var boards = await officialPages.WriteAsync(staging, manifest, officials, additions, removed, previous, statuses, billLines, rewriteOfficials, cancellationToken);
        await indexPages.WriteHomeAsync(staging, manifest, facets, HomeHighlights.From(manifest, boards, officials.Officials.Length) with { BillLines = billLines }, additions, previous, cancellationToken);
        await staging.WriteSiteFilesAsync(manifest, cancellationToken);
        await staging.WriteFileAsync(PublicationProtocol.ManifestPath, manifestJson, cancellationToken);
        return (await staging.CompleteAsync(number, cancellationToken), entries, new ReleaseNames([.. officials.Officials], facets));
    }

    private async Task WriteWithdrawalPagesAsync(IReleaseStaging staging, List<PublishedWithdrawal> withdrawals,
        CancellationToken cancellationToken)
    {
        foreach (var withdrawal in withdrawals)
            await staging.WriteFileAsync(PublicationProtocol.RecordPagePath(withdrawal.RecordId),
                Encoding.UTF8.GetBytes(await renderer.RenderWithdrawalAsync(withdrawal, cancellationToken)), cancellationToken);
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
        catch (Exception exception) when (exception is PublicationConflictException or PublicationStateMismatchException or ArgumentException)
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
