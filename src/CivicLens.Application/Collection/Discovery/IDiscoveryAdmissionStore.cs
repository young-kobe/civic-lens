namespace CivicLens.Application.Collection.Discovery;

public interface IDiscoveryAdmissionStore
{
    /// <summary>Resolve a configured request after atomically reading its key; existing keys retain their saved admission date.</summary>
    Task<DiscoveryAdmissionResult> AdmitAsync(ConfiguredCollectionSource source, string attemptId,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<DiscoveryAdmissionResult> AdmitAsync(DiscoveryAdmissionRequest request, CancellationToken cancellationToken);
}
