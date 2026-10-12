using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class GetStatementDraftRun(IStatementDraftStore store)
{
    public Task<StatementDraftRun?> ExecuteAsync(ReviewActor actor, string runId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (runId is not { Length: 32 } || !runId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("The run ID must be 32 lowercase hexadecimal characters.", nameof(runId));
        return store.GetRunAsync(runId, cancellationToken);
    }
}
