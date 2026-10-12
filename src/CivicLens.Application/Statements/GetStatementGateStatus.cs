using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed record StatementGateStatusReport(DocumentChangeAnalysisStatus? Status, long ChargedToday,
    IReadOnlyDictionary<StatementGateView, int> Counts);

public sealed class GetStatementGateStatus(IStatementAnalysisStore store, IDocumentChangeAnalysisStatusStore status)
{
    public async Task<StatementGateStatusReport> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return new(await status.GetAsync(cancellationToken), await store.GetChargedTodayAsync(cancellationToken),
            await store.CountAsync(cancellationToken));
    }
}
