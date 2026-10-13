using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection.Bills;

public interface IBillStreamAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        BillStreamWindow window, CongressBillListPage page, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
        CongressBill bill, BillGateOutcome gate, string issueRevisionId, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitCosponsorPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillCosponsorsPage page, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitActionsPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillActionsPage page, CancellationToken cancellationToken);
}
