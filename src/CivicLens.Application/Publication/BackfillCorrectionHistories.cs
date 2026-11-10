using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class BackfillCorrectionHistories(IPublicationStore publications, IReleaseDirectory releases)
{
    private const int MaximumReportedFailures = 50;
    private const int BatchSize = 25;

    public async Task<CorrectionHistoryBackfillResult> ExecuteAsync(ReviewActor actor, int limit,
        CorrectionHistoryBootstrapCursor? after, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var examined = 0;
        var cursor = after;
        CorrectionHistoryBootstrapCursor? continuation = null;
        var failures = new List<CorrectionHistoryBackfillFailure>();
        var failed = 0;
        var added = 0;
        var alreadyPresent = 0;
        while (examined < limit)
        {
            var pageLimit = Math.Min(BatchSize, limit - examined);
            var page = await publications.ReadCorrectionHistoryBootstrapPageAsync(cursor, pageLimit,
                releases.GetActiveAsync, cancellationToken);
            if (page.Candidates.Count == 0) break;
            examined += page.Candidates.Count;
            var ready = new List<(PublicationReleaseSummary Release, PublicationCorrectionHistory History)>();
            foreach (var candidate in page.Candidates)
            {
                try
                {
                    var row = candidate.Record;
                    if (row.FirstPublishedAtUtc is null || row.Facts is null)
                        throw new InvalidDataException("Stored source facts are incomplete.");
                    var json = await ReleaseRecordFiles.ReadAsync(releases, candidate.Release.DirectoryName, row.RecordId, cancellationToken);
                    var (fileEntry, corrections) = RecordFileEntry.ReadContent(row.Kind, json);
                    if (fileEntry.Kind != row.Kind || fileEntry.RecordId != row.RecordId || fileEntry.RevisionNumber != row.RevisionNumber)
                        throw new InvalidDataException("Record identity differs from stored release state.");
                    var expected = ReleaseSplice.EntryOf(row, fileEntry);
                    if (!ReleaseEntryComparison.SameOutsideNewFields(fileEntry, expected) ||
                        fileEntry.ApprovedBy is { } approver && expected.ApprovedBy != approver)
                        throw new InvalidDataException("Record facts differ from stored release state.");
                    ready.Add((candidate.Release, new PublicationCorrectionHistory(
                        new PublishedRevisionBinding(row.Kind, row.RecordId, row.RevisionNumber), corrections)));
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException)
                {
                    failed++;
                    if (failures.Count < MaximumReportedFailures)
                        failures.Add(new(candidate.Release.ReleaseNumber, PublishedRecordReference.NameOf(candidate.Record.Kind),
                            candidate.Record.RecordId, ReasonOf(exception)));
                }
            }

            foreach (var group in ready.GroupBy(item => item.Release.ReleaseNumber))
            {
                var release = group.First().Release;
                var histories = group.Select(item => item.History).ToArray();
                var bindings = await publications.PrepareCorrectionHistoriesAsync(histories, cancellationToken);
                var result = await publications.CommitCorrectionHistoryBootstrapAsync(release, bindings, releases.GetActiveAsync,
                    cancellationToken);
                added += result.Added;
                alreadyPresent += result.AlreadyPresent;
            }

            if (page.NextCursor is null) break;
            cursor = page.NextCursor;
            if (examined == limit)
            {
                continuation = page.NextCursor;
                break;
            }
        }

        return new(examined, added, alreadyPresent, failed, failures, continuation?.Encode());
    }

    private static string ReasonOf(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "recordMissing",
        IOException or UnauthorizedAccessException => "recordUnreadable",
        _ => "recordInvalid"
    };
}
