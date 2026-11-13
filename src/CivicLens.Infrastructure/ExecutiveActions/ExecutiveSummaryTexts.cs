using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.ExecutiveActions;

/// <summary>The one reader of the stored body text of a record, and the one check that binds a summary to it. The step reads the text here.
/// Approval checks the binding here, and publication will check it here.</summary>
internal static class ExecutiveSummaryTexts
{
    public static Task<ExecutiveSummaryText?> ReadAsync(CollectionAttemptDbContext db, string recordId, CancellationToken cancellationToken) =>
        (from document in db.Set<ExecutiveActionDocumentRow>().AsNoTracking()
         join text in db.Set<ExecutiveActionTextRow>().AsNoTracking()
             on new { document.SourceId, document.DocumentNumber } equals new { text.SourceId, text.DocumentNumber }
         join extraction in db.Set<DocumentExtractionRow>().AsNoTracking() on text.ExtractionId equals extraction.ExtractionId
         where document.RecordId == recordId && document.NoticeUrl == null
         select new ExecutiveSummaryText(extraction.ExtractionId, extraction.ExtractionVersion, extraction.Text, text.CharsRead, text.CharsTotal))
        .SingleOrDefaultAsync(cancellationToken);

    /// <summary>A summary quotes the stored text of its own document, and its cut flag equals the stored counts. A span of another extraction fails.</summary>
    public static async Task RequireBoundAsync(CollectionAttemptDbContext db, ExecutiveActionRevision revision, CancellationToken cancellationToken)
    {
        if (revision.Summary is not { } summary) return;
        var text = await db.Set<ExecutiveActionTextRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.SourceId == revision.SourceId && row.DocumentNumber == revision.DocumentNumber, cancellationToken);
        if (text is null || summary.Sentences[0].Quote.ExtractionId != text.ExtractionId)
            throw new ArgumentException("The summary quotes an extraction that is not the stored text of its document.");
        if (summary.Cut != text.CharsRead < text.CharsTotal)
            throw new ArgumentException("The cut flag of the summary differs from the stored character counts of the text.");
    }
}
