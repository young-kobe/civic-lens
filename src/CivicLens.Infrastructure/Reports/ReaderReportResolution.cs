using CivicLens.Application.Reports;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Reports.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace CivicLens.Infrastructure.Reports;

internal static class ReaderReportResolution
{
    public const string CorrectionNote = "Approved with a correction.";

    public static void Resolve(UpdateSettersBuilder<ReaderReportRow> set, ReaderReportStatus status, string? note, string actorSubject,
        long nowTicks) => set
        .SetProperty(row => row.Status, status)
        .SetProperty(row => row.Note, note)
        .SetProperty(row => row.ResolvedBy, actorSubject)
        .SetProperty(row => row.ResolvedAtUtcTicks, nowTicks)
        .SetProperty(row => row.Version, row => row.Version + 1);

    /// <summary>Closes the listed open reports of the record whose revision is older than the approved revision. The caller runs
    /// this in the transaction of the approval.</summary>
    public static async Task CloseCorrectedAsync(CollectionAttemptDbContext db, string actorSubject, PublishedRecordKind kind,
        string recordId, IReadOnlyCollection<Guid> reportIds, int approvedRevisionNumber, CancellationToken cancellationToken)
    {
        if (reportIds.Count == 0) return;
        var now = await DatabaseClock.ReadAsync(db, cancellationToken);
        await db.Set<ReaderReportRow>()
            .Where(row => reportIds.Contains(row.ReportId) && row.Kind == kind && row.RecordId == recordId && row.Status == ReaderReportStatus.Open &&
                row.RevisionNumber < approvedRevisionNumber)
            .ExecuteUpdateAsync(set => Resolve(set, ReaderReportStatus.CorrectionNeeded, CorrectionNote, actorSubject, now.Ticks), cancellationToken);
    }
}
