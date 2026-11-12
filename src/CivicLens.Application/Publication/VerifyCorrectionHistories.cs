using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class VerifyCorrectionHistories(IPublicationStore publications, IReleaseDirectory releases)
{
    private const int BatchSize = 25;
    private const int MaximumReportedFailures = 50;

    public async Task<CorrectionHistoryVerificationResult> ExecuteAsync(ReviewActor actor, int limit,
        CorrectionHistoryVerificationCursor? after, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var examined = 0;
        var verified = 0;
        var failures = new List<CorrectionHistoryBackfillFailure>();
        var failed = 0;
        var verifiedReleases = new List<CorrectionHistoryRelease>();
        var skippedReleases = new List<CorrectionHistorySkippedRelease>();
        var cursor = after;
        var releaseFailures = after?.PriorFailures ?? 0;
        do
        {
            var page = await publications.ReadCorrectionHistoryVerificationPageAsync(cursor, Math.Min(BatchSize, limit - examined),
                releases.GetActiveAsync, cancellationToken);
            if (page.Skipped is not null)
            {
                skippedReleases.Add(page.Skipped);
                releaseFailures = 0;
            }
            if (page.Release is null)
            {
                cursor = null;
                break;
            }
            var pageFailed = 0;
            foreach (var candidate in page.Candidates)
            {
                examined++;
                if (await VerifyCandidateAsync(candidate, failures, cancellationToken)) verified++;
                else pageFailed++;
            }
            failed += pageFailed;
            releaseFailures += pageFailed;
            if (!page.ReleaseCompleted)
            {
                cursor = new(page.Release.ReleaseNumber, page.Release.DirectoryName, ReferenceOf(page.Candidates[^1].Record), releaseFailures);
            }
            else
            {
                // A release counts as verified only when no record failed in this call or in an earlier call that the cursor carries.
                if (releaseFailures == 0) verifiedReleases.Add(new(page.Release.ReleaseNumber, page.Release.DirectoryName));
                releaseFailures = 0;
                cursor = page.NextRelease is null ? null : new(page.NextRelease.ReleaseNumber, page.NextRelease.DirectoryName);
            }
        } while (cursor is not null && examined < limit);
        // File reads run outside the short database snapshots, so the reported selection is read again after the last file.
        var selection = await publications.ReadRetainedReleaseSelectionAsync(releases.GetActiveAsync, cancellationToken);
        return new(examined, verified, failed, failures, cursor?.Encode(), verifiedReleases, skippedReleases,
            [.. selection.Select(release => new CorrectionHistorySelectedRelease(release.ReleaseNumber, release.DirectoryName,
                new CorrectionHistoryVerificationCursor(release.ReleaseNumber, release.DirectoryName).Encode()))]);
    }

    private async Task<bool> VerifyCandidateAsync(CorrectionHistoryVerificationCandidate candidate,
        List<CorrectionHistoryBackfillFailure> failures, CancellationToken cancellationToken)
    {
        try
        {
            Verify(candidate, await ReleaseRecordFiles.ReadAsync(releases, candidate.Release.DirectoryName,
                candidate.Record.RecordId, cancellationToken));
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            if (failures.Count < MaximumReportedFailures)
                failures.Add(new(candidate.Release.ReleaseNumber, PublishedRecordReference.NameOf(candidate.Record.Kind),
                    candidate.Record.RecordId, ReasonOf(exception)));
            return false;
        }
    }

    private static PublishedRecordReference ReferenceOf(StoredReleaseRecord record) => new(record.Kind, record.RecordId);

    private static void Verify(CorrectionHistoryVerificationCandidate candidate, byte[] json)
    {
        var row = candidate.Record;
        if (candidate.StoredHistoryError is not null)
            throw new InvalidDataException("Stored correction history is malformed.");
        if (row.FirstPublishedAtUtc is null || row.Facts is null)
            throw new InvalidDataException("Stored source facts are incomplete.");
        var (entry, corrections) = RecordFileEntry.ReadContent(row.Kind, json);
        if (entry.Kind != row.Kind || entry.RecordId != row.RecordId || entry.RevisionNumber != row.RevisionNumber)
            throw new InvalidDataException("Record identity differs from stored release state.");
        var expected = ReleaseSplice.EntryOf(row, entry);
        if (!ReleaseEntryComparison.SameOutsideNewFields(entry, expected) ||
            entry.ApprovedBy is { } approver && expected.ApprovedBy != approver ||
            entry.ApprovedAtUtc is { } approvedAt && expected.ApprovedAtUtc != approvedAt)
            throw new InvalidDataException("Record facts differ from stored release state.");
        var stored = candidate.StoredHistory ?? throw new InvalidDataException("Release correction history binding is missing.");
        if (stored.Binding.Record.Kind != row.Kind || stored.Binding.Record.RecordId != row.RecordId ||
            stored.Binding.Record.RevisionNumber != row.RevisionNumber ||
            !string.Equals(stored.Binding.HistoryId, PublishedCorrectionHistoryIdentity.Of(corrections), StringComparison.Ordinal) ||
            !JsonSerializer.Serialize(corrections, PublicationProtocol.JsonOptions)
                .Equals(JsonSerializer.Serialize(stored.Corrections, PublicationProtocol.JsonOptions), StringComparison.Ordinal))
            throw new InvalidDataException("Release correction history differs from the immutable record file.");
    }

    private static string ReasonOf(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "recordMissing",
        IOException or UnauthorizedAccessException => "recordUnreadable",
        _ => "recordInvalid"
    };
}
