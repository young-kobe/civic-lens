using CivicLens.Application.Analysis;
using CivicLens.Application.Paging;

namespace CivicLens.Tests.Application.Analysis;

internal sealed class FakeReviewOutcomeStore(IReadOnlyList<AiDraftOutcome> outcomes) : IDocumentChangeReviewOutcomeStore
{
    public List<int> Limits { get; } = [];

    public Task<KeysetSlice<AiDraftOutcome>> ListAiDraftsAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        Limits.Add(limit);
        var start = cursor is null ? 0 : (int)cursor.Ticks;
        var page = outcomes.Skip(start).Take(limit).ToArray();
        var next = start + page.Length;
        return Task.FromResult(new KeysetSlice<AiDraftOutcome>([.. page], null,
            next < outcomes.Count ? new PageCursor(PageDirection.Older, next, "x").Encode() : null));
    }
}
