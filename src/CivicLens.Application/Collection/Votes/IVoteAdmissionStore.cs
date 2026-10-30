using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.VoteviewVotes;

namespace CivicLens.Application.Collection.Votes;

public interface IVoteAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        HouseVoteListPage page, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitMembersAsync(ConfiguredCollectionSource source, string membersJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitVoteviewFollowOnsAsync(ConfiguredCollectionSource source, ApiResource finished, string jobId,
        int congress, CancellationToken cancellationToken);

    /// <summary>True when the newest candidate of the Congress came from captures with these three bodies and the same extraction version.</summary>
    Task<bool> HasVoteviewCandidateOfBodiesAsync(ConfiguredCollectionSource source, int congress, VoteviewBodies bodies,
        CancellationToken cancellationToken);

    /// <summary>The roll calls whose text differs from the newest saved version, or that have no version yet.</summary>
    Task<IReadOnlyList<VoteviewRollExtraction>> SelectChangedVoteviewRollsAsync(ConfiguredCollectionSource source, int congress,
        IReadOnlyList<VoteviewRollExtraction> rolls, CancellationToken cancellationToken);

    Task<DiscoveryAdmissionResult> AdmitVoteviewVotesAsync(ConfiguredCollectionSource source, VoteviewChain chain,
        DateTimeOffset votesCapturedAt, IReadOnlyList<VoteviewRollExtraction> rolls, CancellationToken cancellationToken);
}
