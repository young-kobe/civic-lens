using System.Collections.Immutable;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class GetDocumentChangeReviewOutcomes(IDocumentChangeReviewOutcomeStore store)
{
    public async Task<ImmutableArray<DocumentChangeReviewOutcomes>> ExecuteAsync(ReviewActor actor,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        var tallies = new SortedDictionary<string, DocumentChangeReviewOutcomeTally>(StringComparer.Ordinal);
        PageCursor? cursor = null;
        do
        {
            var slice = await store.ListAiDraftsAsync(cursor, PageLimit.Maximum, cancellationToken);
            foreach (var outcome in slice.Items)
            {
                if (!tallies.TryGetValue(outcome.PromptVersion, out var tally))
                    tallies[outcome.PromptVersion] = tally = new DocumentChangeReviewOutcomeTally(outcome.PromptVersion);
                tally.Add(outcome);
            }

            cursor = PageCursor.Parse(slice.OlderCursor);
        }
        while (cursor is not null);
        return [.. tallies.Values.Select(tally => tally.ToOutcomes())];
    }
}
