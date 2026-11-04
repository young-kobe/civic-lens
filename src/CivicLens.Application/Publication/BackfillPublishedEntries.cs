using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Fills the entry rows of revisions that a commit never wrote. The oldest kept folder of a range supplies the entry. The record file must agree
/// with it, and so must the entry in every later kept folder of the range. A range that fails a check gets a skip reason and no row.
/// Each kept manifest is read once, from the oldest folder to the newest. Rows go in only through the store insert.
/// </summary>
public sealed class BackfillPublishedEntries(IPublicationStore publications, IReleaseDirectory releases)
{
    private const int BatchSize = 25;
    private const int ReportedSkips = 50;
    private const string ManifestInvalid = "manifestInvalid";
    private const string RecordFileInvalid = "recordFileInvalid";
    private const string RecordFileDiffers = "recordFileDiffers";
    private const string FoldersDiffer = "foldersDiffer";
    private const string FirstPublishedMissing = "firstPublishedMissing";

    public async Task<PublishedEntriesBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var candidates = await publications.ReadEntryCandidatesAsync(cancellationToken);
        var pending = candidates.Where(candidate => !candidate.HasEntry).Select(candidate => new PendingPublishedEntry(candidate)).ToList();
        foreach (var release in await publications.ListAllAsync(cancellationToken))
            await ReadFolderAsync(release, [.. pending.Where(entry => entry.Candidate.Covers(release.ReleaseNumber))], cancellationToken);
        var (added, racedIn) = await WriteAsync(pending, limit, cancellationToken);
        return Report(added, candidates.Count(candidate => candidate.HasEntry) + racedIn, pending);
    }

    private async Task ReadFolderAsync(PublicationReleaseSummary release, List<PendingPublishedEntry> covered, CancellationToken cancellationToken)
    {
        if (covered.Count == 0) return;
        PublicationRelease manifest;
        try
        {
            manifest = await PreviousRelease.LoadManifestAsync(releases, release, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (InvalidDataException)
        {
            covered.ForEach(entry => entry.Block(ManifestInvalid));
            return;
        }
        var listed = manifest.Records.ToDictionary(entry => new PublishedRecordReference(entry.Kind, entry.RecordId));
        foreach (var entry in covered.Where(entry => !entry.IsBlocked))
            await ReadEntryAsync(release, listed, entry, cancellationToken);
    }

    private async Task ReadEntryAsync(PublicationReleaseSummary release, Dictionary<PublishedRecordReference, PublishedRecordEntry> listed,
        PendingPublishedEntry pending, CancellationToken cancellationToken)
    {
        var candidate = pending.Candidate;
        if (!listed.TryGetValue(new(candidate.Kind, candidate.RecordId), out var entry) || entry.RevisionNumber != candidate.RevisionNumber) return;
        if (pending.Entry is null)
            await ReadFirstEntryAsync(release, entry, pending, cancellationToken);
        else if (!ReleaseEntryComparison.SameOutsideNewFields(pending.Entry, entry))
            pending.Block(FoldersDiffer);
    }

    private async Task ReadFirstEntryAsync(PublicationReleaseSummary release, PublishedRecordEntry entry, PendingPublishedEntry pending,
        CancellationToken cancellationToken)
    {
        PublishedRecordEntry fromFile;
        try
        {
            fromFile = RecordFileEntry.Read(entry.Kind, await releases.ReadFileAsync(release.DirectoryName,
                PublicationProtocol.RecordDataPath(entry.RecordId), PublishedRecordFiles.MaximumCitedRecordBytes, cancellationToken));
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or JsonException)
        {
            pending.Block(RecordFileInvalid);
            return;
        }
        if (ReleaseEntryComparison.SameOutsideNewFields(entry, fromFile)) pending.Entry = entry;
        else pending.Block(RecordFileDiffers);
    }

    private async Task<(int Added, int RacedIn)> WriteAsync(List<PendingPublishedEntry> pending, int limit, CancellationToken cancellationToken)
    {
        var ready = pending.Where(entry => entry.IsReady).ToList();
        int added = 0, racedIn = 0, next = 0;
        while (added < limit && next < ready.Count)
        {
            var batch = ready.Skip(next).Take(Math.Min(BatchSize, limit - added)).ToList();
            next += batch.Count;
            var writable = await WithoutMissingFirstPublicationsAsync(batch, cancellationToken);
            var created = await publications.AddEntriesAsync([.. writable.Select(entry => entry.Entry!)], cancellationToken);
            writable.ForEach(entry => entry.HasRow = true);
            added += created;
            racedIn += writable.Count - created;
        }
        return (added, racedIn);
    }

    private async Task<List<PendingPublishedEntry>> WithoutMissingFirstPublicationsAsync(List<PendingPublishedEntry> batch,
        CancellationToken cancellationToken)
    {
        var stored = await publications.ReadFirstPublishedAsync([.. batch.SelectMany(entry => FirstPublicationsNeededBy(entry.Entry!)).Distinct()],
            cancellationToken);
        foreach (var entry in batch.Where(entry => !FirstPublicationsNeededBy(entry.Entry!).All(stored.ContainsKey)))
            entry.Block(FirstPublishedMissing);
        return [.. batch.Where(entry => !entry.IsBlocked)];
    }

    private static IEnumerable<PublishedRecordReference> FirstPublicationsNeededBy(PublishedRecordEntry entry) =>
        PublishedRecordReference.LinkedBy(entry).Prepend(new(entry.Kind, entry.RecordId));

    private static PublishedEntriesBackfillResult Report(int added, int alreadyPresent, List<PendingPublishedEntry> pending)
    {
        var skipped = pending.Where(entry => entry.IsBlocked).ToList();
        var byReason = skipped.GroupBy(entry => entry.SkipReason!).ToDictionary(group => group.Key, group => group.Count());
        var first = skipped.Take(ReportedSkips).Select(entry => new SkippedPublishedEntry(PublishedRecordReference.NameOf(entry.Candidate.Kind),
            entry.Candidate.RecordId, entry.Candidate.RevisionNumber, entry.SkipReason!)).ToList();
        return new(pending.Count, added, alreadyPresent, skipped.Count, byReason, first,
            pending.Count(entry => entry.Candidate.IsOpen && !entry.HasRow),
            pending.Count(entry => !entry.Candidate.IsOpen && entry.Entry is null && !entry.IsBlocked));
    }
}
