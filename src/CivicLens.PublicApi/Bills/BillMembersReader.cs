using CivicLens.PublicApi.Votes;
using CivicLens.PublicApi.ZipDistricts;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Bills;

/// <summary>
/// Reads how the current members of one ZIP area relate to one bill: sponsor, cosponsor, or neither, and each recorded cast on a roll call vote that names the bill.
/// The bill, its sponsorships, the chambers of its votes, the casts, and the ZIP members must come from one release. When a release change splits them, the read starts again.
/// </summary>
public sealed class BillMembersReader(ZipDistrictStore zips, BillStore bills, VoteStore votes, MemberVoteStore memberVotes)
{
    private const int Attempts = 3;

    /// <summary>A ZIP miss, or no bill, or the members data. Exactly one is set, and a null data with no miss means that no active release holds the bill.</summary>
    public sealed record Answer(BillMembersData? Data, ZipLookupMiss? Miss);

    public async Task<Answer> ReadAsync(ZipDistrictRequest request, string recordId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var lookup = await zips.FindStripAsync(request, null, cancellationToken);
            if (lookup.Answer is not { } zip) return new(null, lookup.Miss);
            if (await bills.FindAsync(recordId, cancellationToken) is not { } detail) return new(null, null);
            if (detail.Release != zip.Release) continue;
            if (await ReadPartsAsync(zip, detail, cancellationToken) is { } data) return new(data, null);
        }
        throw new InvalidOperationException("The active release changed on every read of the bill members.");
    }

    // The chambers and the casts come from reads that are complete for the bill, so a bill with many roll calls never hides a vote or a cast.
    private async Task<BillMembersData?> ReadPartsAsync(ZipDistrictAnswer zip, BillDetail detail, CancellationToken cancellationToken)
    {
        var bill = detail.Bill;
        if (await bills.ListSponsorsAsync(bill.RecordId, cancellationToken) is not { } sponsors || sponsors.Release != detail.Release) return null;
        var chambers = await votes.FindBillChambersAsync(bill.Congress, bill.BillType, bill.BillNumber, cancellationToken);
        if (chambers is null || chambers.Release != detail.Release) return null;
        var seats = ZipMemberCardsMapper.Map(zip, null, [], null).Seats.Where(seat => seat.Member is not null).DistinctBy(seat => seat.Member!.OfficialId).ToArray();
        var casts = await memberVotes.FindBillCastsAsync([.. seats.Select(seat => seat.Member!.OfficialId)], bill.Congress, bill.BillType, bill.BillNumber, cancellationToken);
        if (casts is null || casts.Release != detail.Release) return null;
        var members = seats.Select(seat => Member(seat, sponsors.Rows, casts.Casts)).ToArray();
        return new(zip.Zcta, bill.RecordId, detail.Release, members, chambers.Chambers);
    }

    private static BillMemberData Member(ZipMemberSeatData seat, BillSponsorRow[] sponsorships, MemberBillCast[] casts)
    {
        var member = seat.Member!;
        var own = sponsorships.Where(row => row.OfficialId == member.OfficialId && row.WithdrawnOn is null).ToArray();
        var sponsor = own.FirstOrDefault(row => row.Role == PublishedSponsorshipRole.Sponsor);
        var cosponsor = own.FirstOrDefault(row => row.Role == PublishedSponsorshipRole.Cosponsor);
        var relation = sponsor is not null ? BillMemberRelation.Sponsor : cosponsor is not null ? BillMemberRelation.Cosponsor : BillMemberRelation.None;
        var memberCasts = casts.Where(cast => cast.OfficialId == member.OfficialId && cast.Chamber == seat.Chamber)
            .Select(cast => new BillMemberCastData(cast.VotedOn, cast.CastWords, cast.Side));
        return new(member.Name, seat.Chamber, relation, cosponsor?.SponsoredOn, [.. memberCasts]);
    }
}
