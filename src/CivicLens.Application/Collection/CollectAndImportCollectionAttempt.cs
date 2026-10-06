using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

/// <summary>Collects and durably hands off a verified result before attempting its evidence import.</summary>
public sealed class CollectAndImportCollectionAttempt(ICollectorProcess collector, ICollectionAttemptStore store,
    ICollectionReceiptHandoffStore handoffs)
{
    public async Task<CollectionAttemptCompletion> ExecuteAsync(string attemptId, CollectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!PendingCollectionHandoff.IsValidAttemptId(attemptId))
            throw new ArgumentException("Attempt ID must be 1 to 128 ASCII letters, digits, dashes, or underscores.", nameof(attemptId));
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        await using var lease = await handoffs.AcquireRecoveryLeaseAsync(cancellationToken);
        var result = await collector.RunAsync(request, cancellationToken);
        result.ValidateAgainst(request);
        cancellationToken.ThrowIfCancellationRequested();

        var handoff = new PendingCollectionHandoff(PendingCollectionHandoff.CurrentVersion, attemptId, request, result);
        handoff.Validate();
        await handoffs.SaveAsync(handoff, cancellationToken);
        var decision = await CollectionAttemptImporter.ImportAsync(attemptId, request, result, store, cancellationToken);
        return await RemoveHandoffAsync(attemptId, decision, handoffs, cancellationToken);
    }

    internal static async Task<CollectionAttemptCompletion> RemoveHandoffAsync(string id,
        CivicLens.Core.Collection.CollectionImportDecision decision, ICollectionReceiptHandoffStore handoffs,
        CancellationToken cancellationToken)
    {
        try
        {
            await handoffs.DeleteAsync(id, cancellationToken);
            return new CollectionAttemptCompletion(decision, true, null);
        }
        catch (OperationCanceledException)
        {
            return new CollectionAttemptCompletion(decision, false, "Handoff cleanup was interrupted; pending receipt was retained.");
        }
        catch (Exception)
        {
            return new CollectionAttemptCompletion(decision, false, "Handoff cleanup failed; pending receipt was retained.");
        }
    }
}
