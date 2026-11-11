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
        var failed = 0;
        var cursor = after;
        CorrectionHistoryVerificationCursor? next = null;
        var selectionFingerprint = "";
        var failures = new List<CorrectionHistoryBackfillFailure>();
        while (examined < limit)
        {
            var page = await publications.ReadCorrectionHistoryVerificationPageAsync(cursor, Math.Min(BatchSize, limit - examined),
                releases.GetActiveAsync, cancellationToken);
            if (page.StaleCursor)
                return new(examined, verified, failed, failures, null, true) { SelectionFingerprint = page.SelectionFingerprint };
            selectionFingerprint = page.SelectionFingerprint;
            if (page.Candidates.Count == 0) break;
            foreach (var candidate in page.Candidates)
            {
                examined++;
                try
                {
                    Verify(candidate, await ReleaseRecordFiles.ReadAsync(releases, candidate.Release.DirectoryName,
                        candidate.Record.RecordId, cancellationToken));
                    verified++;
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException)
                {
                    failed++;
                    if (failures.Count < MaximumReportedFailures)
                        failures.Add(new(candidate.Release.ReleaseNumber, PublishedRecordReference.NameOf(candidate.Record.Kind),
                            candidate.Record.RecordId, ReasonOf(exception)));
                }
            }
            if (page.NextCursor is null) break;
            cursor = page.NextCursor;
            if (examined >= limit)
            {
                next = page.NextCursor;
                break;
            }
        }
        if (next is null)
        {
            // File reads run outside the short database snapshot, so refresh selection once before declaring the scan terminal.
            var currentFingerprint = await publications.ReadRetainedReleaseFingerprintAsync(releases.GetActiveAsync, cancellationToken);
            if (currentFingerprint != selectionFingerprint)
                return new(examined, verified, failed, failures, null, true) { SelectionFingerprint = currentFingerprint };
        }
        return new(examined, verified, failed, failures, next?.Encode(), false) { SelectionFingerprint = selectionFingerprint };
    }

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
            entry.ApprovedBy is { } approver && expected.ApprovedBy != approver)
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
