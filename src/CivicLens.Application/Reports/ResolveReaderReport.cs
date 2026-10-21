using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Reports;

public sealed class ResolveReaderReport(IReaderReportStore store)
{
    public const int MaximumNoteCharacters = 2000;

    public Task ExecuteAsync(ReviewActor actor, ResolveReaderReportRequest request, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Status is not (ReaderReportStatus.Dismissed or ReaderReportStatus.CorrectionNeeded))
            throw new ArgumentException("A report resolves as dismissed or as needing a correction.", nameof(request));
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is null && request.Status == ReaderReportStatus.CorrectionNeeded)
            throw new ArgumentException("A report that needs a correction needs a note.", nameof(request));
        if (note is not null && (note.Length > MaximumNoteCharacters || note.Contains('\0', StringComparison.Ordinal)))
            throw new ArgumentException($"A note holds up to {MaximumNoteCharacters} characters and no NUL character.", nameof(request));
        return store.ResolveAsync(actor.Subject, request.ReportId, request.ExpectedVersion, request.Status, note, cancellationToken);
    }
}
