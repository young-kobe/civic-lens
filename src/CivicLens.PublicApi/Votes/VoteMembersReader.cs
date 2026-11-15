using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.ZipDistricts;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Votes;

/// <summary>
/// Reads how the current members of one ZIP area voted on one roll call: the cast, the party on the vote, and the party line from <c>public_api.member_votes.breaks_party</c>.
/// The ZIP members and the casts must come from one release. When a release change splits them, the read starts again, as <see cref="Bills.BillMembersReader"/> does.
/// </summary>
public sealed class VoteMembersReader(ZipDistrictStore zips, MemberVoteStore memberVotes)
{
    private const int Attempts = 3;

    /// <summary>A ZIP miss, or no vote, or the members data. Exactly one is set, and a null data with no miss means that no active release holds the vote.</summary>
    public sealed record Answer(VoteMembersData? Data, ZipLookupMiss? Miss);

    public async Task<Answer> ReadAsync(ZipDistrictRequest request, string recordId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var lookup = await zips.FindStripAsync(request, null, cancellationToken);
            if (lookup.Answer is not { } zip) return new(null, lookup.Miss);
            var seats = ZipMemberCardsMapper.Map(zip, null, [], null).Seats;
            var officials = seats.Where(seat => seat.Member is not null).Select(seat => seat.Member!.OfficialId).Distinct(StringComparer.Ordinal).ToArray();
            if (await memberVotes.FindVoteCastsAsync(officials, recordId, cancellationToken) is not { } casts) return new(null, null);
            if (casts.Release != zip.Release) continue;
            return casts.Chamber is { } chamber ? new(Map(zip.Zcta, recordId, zip.Release, chamber, seats, casts.Casts), null) : new(null, null);
        }
        throw new InvalidOperationException("The active release changed on every read of the vote members.");
    }

    private static VoteMembersData Map(string zcta, string recordId, int release, PublishedChamber chamber, ZipMemberSeatData[] seats, MemberVoteCast[] casts)
    {
        var inChamber = seats.Where(seat => seat.Chamber == chamber).ToArray();
        var members = inChamber.Where(seat => seat.Member is not null).DistinctBy(seat => seat.Member!.OfficialId).Select(seat =>
        {
            var member = seat.Member!;
            var cast = casts.FirstOrDefault(row => row.OfficialId == member.OfficialId);
            return new VoteZipMemberData(member.OfficialId, member.Name, seat.Title, cast?.Cast, cast?.Party, cast?.BreaksParty);
        }).ToArray();
        var otherChamber = seats.Where(seat => seat.Chamber != chamber && seat.Member is not null).Select(seat => seat.Member!.OfficialId).Distinct(StringComparer.Ordinal).Count();
        return new(zcta, recordId, release, chamber, members, otherChamber, inChamber.Length > 0 && members.Length == 0);
    }
}
