using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Fills the entry rows of revisions that a commit never wrote. The oldest kept folder of a range supplies the entry. The record file must agree
/// with it, and so must the entry in every later kept folder of the range. A kept folder of the range that omits the revision fails the check too.
/// A range that fails a check gets a skip reason and no row.
/// It also fills the bill line of action rows that hold none. The line is the one that the kept folders of the range hold. Folders from before the
/// line hold none, and two folders that hold different lines fail the check. A row that holds a line is never changed.
/// It also pins the bill statuses of each kept release that lacks them, from the status file of its folder. A release from before the file pins none.
/// A row that exists is never changed.
/// The release list is read before the candidates, so a commit between the two reads leaves a range that no listed folder covers.
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
        var keptReleases = await publications.ListAllAsync(cancellationToken);
        var candidates = await publications.ReadEntryCandidatesAsync(cancellationToken);
        var pending = candidates.Where(candidate => candidate.NeedsBackfill).Select(candidate => new PendingPublishedEntry(candidate)).ToList();
        foreach (var release in keptReleases)
            await ReadFolderAsync(release, [.. pending.Where(entry => entry.Candidate.Covers(release.ReleaseNumber))], cancellationToken);
        var (added, racedIn) = await WriteRowsAsync([.. pending.Where(entry => entry.WritesRow)], limit, cancellationToken);
        var filled = await FillBillLinesAsync([.. pending.Where(entry => !entry.WritesRow)], cancellationToken);
        foreach (var batch in pending.Where(entry => !entry.WritesRow && entry.IsReady && entry.Entry!.ApprovedBy is not null).Chunk(BatchSize))
        {
            await publications.FillStatementApproversAsync([.. batch.Select(entry => entry.EntryToInsert!)], cancellationToken);
            foreach (var entry in batch) entry.ApproverFilled = true;
        }
        var statusesPinned = await PinBillStatusesAsync(keptReleases, cancellationToken);
        return Report(added, filled, statusesPinned, candidates.Count(candidate => candidate.HasEntry) + racedIn, pending);
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
        if (!listed.TryGetValue(new(candidate.Kind, candidate.RecordId), out var entry) || entry.RevisionNumber != candidate.RevisionNumber)
            pending.Block(FoldersDiffer);
        else if (!pending.TryAddBillLine(entry.BillLine))
            pending.Block(FoldersDiffer);
        else if (pending.Entry is null)
            await ReadFirstEntryAsync(release, entry, pending, cancellationToken);
        else if (entry.ApprovedBy is { } approver && pending.Entry.ApprovedBy != approver ||
                 !ReleaseEntryComparison.SameOutsideNewFields(pending.Entry, entry))
            pending.Block(FoldersDiffer);
    }

    private async Task ReadFirstEntryAsync(PublicationReleaseSummary release, PublishedRecordEntry entry, PendingPublishedEntry pending,
        CancellationToken cancellationToken)
    {
        if (pending.Candidate.HasEntry && !pending.Candidate.LacksStatementApprover)
        {
            pending.Entry = entry;
            return;
        }
        PublishedRecordEntry fromFile;
        try
        {
            fromFile = RecordFileEntry.Read(entry.Kind, await ReleaseRecordFiles.ReadAsync(releases, release.DirectoryName, entry.RecordId,
                cancellationToken));
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or JsonException)
        {
            pending.Block(RecordFileInvalid);
            return;
        }
        if (ReleaseEntryComparison.SameOutsideNewFields(entry, fromFile) &&
            (entry.ApprovedBy is null || entry.ApprovedBy == fromFile.ApprovedBy))
            pending.Entry = entry with { ApprovedBy = fromFile.ApprovedBy };
        else pending.Block(RecordFileDiffers);
    }

    private async Task<int> FillBillLinesAsync(List<PendingPublishedEntry> pending, CancellationToken cancellationToken)
    {
        var ready = pending.Where(entry => entry.IsReady && entry.BillLine is not null).ToList();
        var filled = 0;
        foreach (var batch in ready.Chunk(BatchSize))
            filled += await publications.FillBillLinesAsync([.. batch.Select(entry => entry.EntryToInsert!)], cancellationToken);
        return filled;
    }

    private async Task<int> PinBillStatusesAsync(IReadOnlyList<PublicationReleaseSummary> keptReleases, CancellationToken cancellationToken)
    {
        var pinned = 0;
        foreach (var release in keptReleases)
        {
            var statuses = await PreviousRelease.ReadBillStatusesAsync(releases, release.DirectoryName, cancellationToken);
            pinned += await publications.AddBillStatusesAsync(release.ReleaseNumber, [.. statuses.Values], cancellationToken);
        }
        return pinned;
    }

    private async Task<(int Added, int RacedIn)> WriteRowsAsync(List<PendingPublishedEntry> pending, int limit, CancellationToken cancellationToken)
    {
        var ready = pending.Where(entry => entry.IsReady).ToList();
        int added = 0, racedIn = 0, next = 0;
        while (added < limit && next < ready.Count)
        {
            var batch = ready.Skip(next).Take(Math.Min(BatchSize, limit - added)).ToList();
            next += batch.Count;
            var writable = await WithoutMissingFirstPublicationsAsync(batch, cancellationToken);
            var created = await publications.AddEntriesAsync([.. writable.Select(entry => entry.EntryToInsert!)], cancellationToken);
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

    private static PublishedEntriesBackfillResult Report(int added, int filled, int statusesPinned, int alreadyPresent, List<PendingPublishedEntry> all)
    {
        var pending = all.Where(entry => entry.WritesRow).ToList();
        var skipped = all.Where(entry => entry.IsBlocked).ToList();
        var byReason = skipped.GroupBy(entry => entry.SkipReason!).ToDictionary(group => group.Key, group => group.Count());
        var first = skipped.Take(ReportedSkips).Select(entry => new SkippedPublishedEntry(PublishedRecordReference.NameOf(entry.Candidate.Kind),
            entry.Candidate.RecordId, entry.Candidate.RevisionNumber, entry.SkipReason!)).ToList();
        return new(pending.Count, added, filled, statusesPinned, alreadyPresent, skipped.Count, byReason, first,
            pending.Count(entry => entry.Candidate.IsOpen && !entry.HasRow),
            pending.Count(entry => !entry.Candidate.IsOpen && entry.Entry is null && !entry.IsBlocked),
            pending.Count(entry => entry.IsReady && !entry.HasRow))
        {
            OpenStatementsWithoutApprover = all.Count(entry => entry.Candidate.IsOpen && entry.Candidate.LacksStatementApprover && !entry.ApproverFilled)
        };
    }
}
