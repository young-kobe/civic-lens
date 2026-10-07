namespace CivicLens.Application.Collection.Discovery;

public interface IDiscoveryAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitAsync(DiscoveryAdmissionRequest request, CancellationToken cancellationToken);
}
