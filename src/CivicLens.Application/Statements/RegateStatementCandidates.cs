using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class RegateStatementCandidates(IStatementGateStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, string issueRevisionId, DateOnly? since, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ReviewValidation.ValidateHash(issueRevisionId, nameof(issueRevisionId));
        return store.RegateAsync(issueRevisionId, since, cancellationToken);
    }
}
