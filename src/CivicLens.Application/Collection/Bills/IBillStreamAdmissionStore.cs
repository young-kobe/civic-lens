using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection.Bills;

public interface IBillStreamAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        BillStreamWindow window, CongressBillListPage page, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
        CongressBill bill, BillGateOutcome gate, string issueRevisionId, string extractionId,
        CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitCosponsorPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillCosponsorsPage page, string extractionId,
        CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitActionsPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillActionsPage page, string extractionId,
        CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitSummariesPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillSummariesPage page, string extractionId,
        CancellationToken cancellationToken);
}
