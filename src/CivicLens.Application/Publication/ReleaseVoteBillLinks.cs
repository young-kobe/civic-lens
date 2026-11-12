using CivicLens.Application.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The vote links that one kept release must store. The backfill writes them and the verifier compares them with the stored rows, so both read the one rule
/// of <see cref="ReleaseBillLines"/> over the manifest. That rule links the lowest-ID action entry of the bill in the release, which is the old min(record_id) rule
/// applied to the entries of this release.
/// </summary>
internal static class ReleaseVoteBillLinks
{
    public static async Task<IReadOnlyList<ReleaseVoteBill>> ExpectedAsync(IVoteReviewStore votes, PublicationRelease manifest, CancellationToken cancellationToken)
    {
        var voteBillActions = await votes.FindBillActionRecordIdsOfVotesAsync(
            [.. manifest.Records.Where(entry => entry.Kind == PublishedRecordKind.Vote).Select(PublishedRevisionBinding.Of)], cancellationToken);
        return [.. ReleaseBillLines.From(manifest.Records, voteBillActions).VoteBills()];
    }
}
