using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.ExecutiveActions;

/// <summary>The one reader of the stored body text of a record, and the one check that binds a summary to it. The step reads the text here.
/// Approval and publication check the binding here.</summary>
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

    /// <summary>Each summary quotes the stored text of its own document, and its cut flag equals the stored counts. A span of another extraction fails.
    /// One read serves all revisions. Approval passes one revision, and publication passes the whole batch.</summary>
    public static async Task RequireBoundAsync(CollectionAttemptDbContext db, IReadOnlyCollection<ExecutiveActionRevision> revisions,
        CancellationToken cancellationToken)
    {
        var summarized = revisions.Where(revision => revision.Summary is not null).ToArray();
        if (summarized.Length == 0) return;
        var numbers = summarized.Select(revision => revision.DocumentNumber).Distinct().ToArray();
        var texts = await db.Set<ExecutiveActionTextRow>().AsNoTracking()
            .Where(row => numbers.Contains(row.DocumentNumber)).ToListAsync(cancellationToken);
        foreach (var revision in summarized)
            RequireBound(revision, texts.SingleOrDefault(row => row.SourceId == revision.SourceId && row.DocumentNumber == revision.DocumentNumber));
    }

    private static void RequireBound(ExecutiveActionRevision revision, ExecutiveActionTextRow? text)
    {
        var summary = revision.Summary!;
        if (text is null || summary.Sentences[0].Quote.ExtractionId != text.ExtractionId)
            throw new ArgumentException("The summary quotes an extraction that is not the stored text of its document.");
        if (summary.Cut != text.CharsRead < text.CharsTotal)
            throw new ArgumentException("The cut flag of the summary differs from the stored character counts of the text.");
    }
}
