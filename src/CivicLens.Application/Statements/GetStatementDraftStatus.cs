using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed record StatementDraftStatusReport(AnalysisStatus? Status, long ChargedToday,
    IReadOnlyDictionary<StatementDraftView, int> Counts);

public sealed class GetStatementDraftStatus(IStatementDraftStore store, IAnalysisStatusStore status)
{
    public async Task<StatementDraftStatusReport> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return new(await status.GetAsync(cancellationToken), await store.GetChargedTodayAsync(cancellationToken),
            await store.CountAsync(cancellationToken));
    }
}
