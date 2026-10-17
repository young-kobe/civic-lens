using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class ExportGateLabels(IGateLabelStore store)
{
    public Task<IReadOnlyList<GateLabelExportRow>> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return store.ListLabelsOfRecordAsync(cancellationToken);
    }
}
