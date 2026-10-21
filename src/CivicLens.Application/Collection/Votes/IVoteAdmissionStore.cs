using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.SenateVotes;

namespace CivicLens.Application.Collection.Votes;

public interface IVoteAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        HouseVoteListPage page, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitMembersAsync(ConfiguredCollectionSource source, string membersJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitSenateMenuAsync(ConfiguredCollectionSource source, string menuJobId,
        SenateVoteMenu menu, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitSenateVoteAsync(ConfiguredCollectionSource source, string voteJobId,
        SenateVote vote, string extractionId, CancellationToken cancellationToken);
}
