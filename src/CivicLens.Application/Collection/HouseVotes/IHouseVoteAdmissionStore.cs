using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection.HouseVotes;

public interface IHouseVoteAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        HouseVoteListPage page, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitMembersAsync(ConfiguredCollectionSource source, string membersJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken);
}
