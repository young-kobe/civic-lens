using CivicLens.Application.Statements;
using CivicLens.Core.Review;

namespace CivicLens.Host.Review;

internal static class StatementDraftStart
{
    public static async Task<string> ExecuteAsync(CreateStatementDraft create, ReviewActor actor, string? candidateId,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidateId) || string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Choose a statement to start a draft.");
        try
        {
            _ = await create.ExecuteAsync(actor, new CreateStatementDraftRequest(candidateId, $"{idempotencyKey}:{candidateId}"),
                cancellationToken);
        }
        catch (StatementDraftExistsException) { return StatementRoutes.Editor(candidateId); }
        return StatementRoutes.Editor(candidateId, "started");
    }
}
