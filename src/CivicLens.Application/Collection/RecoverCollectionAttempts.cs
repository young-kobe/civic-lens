using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection;

public sealed class RecoverCollectionAttempts(ICollectionReceiptHandoffStore handoffs,
    ICaptureArtifactVerifier verifier, ICollectionAttemptStore store)
{
    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        handoffs.ListAsync(cancellationToken);

    public async Task<CollectionAttemptCompletion> ReplayAsync(string handoffId, string artifactRoot,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await handoffs.AcquireRecoveryLeaseAsync(cancellationToken);
        var handoff = await LoadAndVerifyAsync(handoffId, artifactRoot, cancellationToken);
        var decision = await CollectionAttemptImporter.ImportAsync(handoff.AttemptId, handoff.Request,
            handoff.Receipt, store, cancellationToken);
        return await CollectAndImportCollectionAttempt.RemoveHandoffAsync(handoffId, decision, handoffs, cancellationToken);
    }

    public async Task<IReadOnlyList<CollectionRecoveryItem>> ReplayAllAsync(string artifactRoot,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await handoffs.AcquireRecoveryLeaseAsync(cancellationToken);
        var ids = await handoffs.ListAsync(cancellationToken);
        var entries = new List<ReplayEntry>(ids.Count);
        foreach (var id in ids)
            entries.Add(await InspectAsync(id, cancellationToken));
        var capturesConfirmed = entries.All(entry => entry.Failure is null);
        var results = new List<CollectionRecoveryItem>(ids.Count);
        foreach (var entry in entries.OrderBy(entry => entry.Outcome == CollectionOutcome.Captured ? 0 : 1))
        {
            var id = entry.Id;
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(Cancelled(id));
                break;
            }
            if (entry.Failure is not null)
            {
                results.Add(entry.Failure);
                continue;
            }
            if (entry.Outcome == CollectionOutcome.NotModified && !capturesConfirmed)
            {
                results.Add(new CollectionRecoveryItem(id, CollectionRecoveryStatus.PrerequisiteUnconfirmed, null,
                    "304 replay requires recovery of pending captured or unreadable handoffs first; it was retained."));
                continue;
            }

            CollectionRecoveryItem result;
            try
            {
                result = await ReplayOneUnderLeaseAsync(id, artifactRoot, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                results.Add(Cancelled(id));
                break;
            }

            results.Add(result);
            if (entry.Outcome == CollectionOutcome.Captured && result.Decision is null)
                capturesConfirmed = false;
            if (cancellationToken.IsCancellationRequested && result.Status == CollectionRecoveryStatus.ImportedCleanupFailed)
                break;
        }
        return results;
    }

    private async Task<ReplayEntry> InspectAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var handoff = await handoffs.LoadAsync(id, cancellationToken);
            handoff.Validate();
            return new ReplayEntry(id, handoff.Receipt.Outcome, null);
        }
        catch (InvalidDataException)
        {
            return new ReplayEntry(id, null, new CollectionRecoveryItem(id, CollectionRecoveryStatus.InvalidHandoff,
                null, "Handoff failed validation; it was retained."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new ReplayEntry(id, null, new CollectionRecoveryItem(id, CollectionRecoveryStatus.RecoveryFailed,
                null, "Recovery failed; the handoff was retained."));
        }
    }

    private static CollectionRecoveryItem Cancelled(string id) =>
        new(id, CollectionRecoveryStatus.Cancelled, null,
            "Recovery was interrupted; the handoff was retained for replay.");

    private async Task<CollectionRecoveryItem> ReplayOneUnderLeaseAsync(string id, string artifactRoot,
        CancellationToken cancellationToken)
    {
        PendingCollectionHandoff handoff;
        try
        {
            handoff = await LoadAndVerifyAsync(id, artifactRoot, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return new CollectionRecoveryItem(id, CollectionRecoveryStatus.InvalidHandoff, null,
                "Handoff failed validation; it was retained.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new CollectionRecoveryItem(id, CollectionRecoveryStatus.RecoveryFailed, null,
                "Recovery failed; the handoff was retained.");
        }

        CollectionImportDecision decision;
        try
        {
            decision = await CollectionAttemptImporter.ImportAsync(handoff.AttemptId, handoff.Request,
                handoff.Receipt, store, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new CollectionRecoveryItem(id, CollectionRecoveryStatus.ImportUnconfirmed, null,
                "Import was not confirmed; the handoff was retained for replay.");
        }

        var completion = await CollectAndImportCollectionAttempt.RemoveHandoffAsync(id, decision, handoffs, cancellationToken);
        return completion.HandoffRemoved
            ? new CollectionRecoveryItem(id, CollectionRecoveryStatus.ImportedAndRemoved, decision, null)
            : new CollectionRecoveryItem(id, CollectionRecoveryStatus.ImportedCleanupFailed, decision, completion.CleanupFailure);
    }

    private async Task<PendingCollectionHandoff> LoadAndVerifyAsync(string handoffId, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var handoff = await handoffs.LoadAsync(handoffId, cancellationToken);
        handoff.Validate();
        await verifier.VerifyAsync(handoff.Request, handoff.Receipt, Path.GetFullPath(artifactRoot), cancellationToken);
        return handoff;
    }

    private sealed record ReplayEntry(string Id, CollectionOutcome? Outcome, CollectionRecoveryItem? Failure);
}
