using CivicLens.Application.Paging;

namespace CivicLens.Application.Analysis;

public interface IDocumentChangeReviewOutcomeStore
{
    Task<KeysetSlice<AiDraftOutcome>> ListAiDraftsAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken);
}
