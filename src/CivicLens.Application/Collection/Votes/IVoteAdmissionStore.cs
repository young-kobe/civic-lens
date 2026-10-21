using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;

namespace CivicLens.Application.Collection.Votes;

public interface IVoteAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        HouseVoteListPage page, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitMembersAsync(ConfiguredCollectionSource source, string membersJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken);
}
