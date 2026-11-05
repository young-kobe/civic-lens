using CivicLens.Application.Analysis;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Analysis;

/// <summary>Finds the document change drafts that a model redraft may replace. The rule has three parts. The current revision
/// is a model revision. No person has written a revision or decided on the draft. The run of the current revision used an old version.</summary>
internal static class StaleDocumentChangeDrafts
{
    public static async Task<Dictionary<string, DocumentChangeModelDraft>> FindAsync(CollectionAttemptDbContext db, string[] comparisonIds,
        CancellationToken cancellationToken)
    {
        var untouched = await (from draft in db.Set<DocumentChangeDraftRow>().AsNoTracking()
                               join revision in db.Set<DocumentChangeRevisionRow>().AsNoTracking()
                                   on new { draft.DraftId, Number = draft.CurrentRevisionNumber } equals new { revision.DraftId, Number = revision.RevisionNumber }
                               where comparisonIds.Contains(draft.ComparisonId) && draft.ReviewStateVersion == 0 &&
                                   draft.ReviewStatus == ReviewStatus.AwaitingReview && revision.AuthorKind == ReviewAuthorKind.Analysis &&
                                   !db.Set<DocumentChangeRevisionRow>().Any(other => other.DraftId == draft.DraftId && other.AuthorKind == ReviewAuthorKind.Person)
                               select new { draft.ComparisonId, draft.DraftId, draft.CurrentRevisionNumber }).ToArrayAsync(cancellationToken);
        var draftIds = untouched.Select(draft => draft.DraftId).ToArray();
        var runs = await db.Set<DocumentChangeAnalysisRunRow>().AsNoTracking().Where(run => draftIds.Contains(run.DraftId!))
            .Select(run => new { run.RunId, DraftId = run.DraftId!, Number = run.RevisionNumber!.Value }).ToArrayAsync(cancellationToken);
        var staleRunIds = await StaleAnalysisRuns.FindStaleRunIdsAsync<DocumentChangeAnalysisRunRow>(db, [.. runs.Select(run => run.RunId)],
            DocumentChangeDraftTask.CurrentVersions, cancellationToken);
        var staleRuns = runs.Where(run => staleRunIds.Contains(run.RunId)).Select(run => (run.DraftId, run.Number)).ToHashSet();
        return untouched.Where(draft => staleRuns.Contains((draft.DraftId, draft.CurrentRevisionNumber)))
            .ToDictionary(draft => draft.ComparisonId, draft => new DocumentChangeModelDraft(draft.DraftId, draft.CurrentRevisionNumber), StringComparer.Ordinal);
    }
}
