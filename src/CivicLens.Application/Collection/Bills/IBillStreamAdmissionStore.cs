using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection.Bills;

public interface IBillStreamAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        BillStreamWindow window, CongressBillListPage page, CancellationToken cancellationToken);
}
