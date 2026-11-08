using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.ZipDistricts;

/// <summary>Maps one ZIP snapshot to presentation inputs without reading any data again.</summary>
internal static class ZipMemberCardsMapper
{
    public static ZipMemberCardsData Map(ZipDistrictAnswer answer)
    {
        var seats = new List<ZipMemberSeatData>();
        var withoutSenators = new List<string>();
        foreach (var district in answer.Districts)
            seats.Add(new(HouseTitle(district), Member(district.Representative)));
        foreach (var state in answer.States)
        {
            var stateName = UsStates.NameOf(state.State);
            if (state.Senators.Length == 0) withoutSenators.Add(stateName);
            foreach (var senator in state.Senators)
                seats.Add(new($"Senator for {stateName}", Member(senator)));
        }
        return new(answer.Release, answer.Congress, answer.Zcta, answer.Districts.Length, [.. seats], [.. withoutSenators]);
    }

    private static string HouseTitle(ZipDistrict district)
    {
        var state = UsStates.NameOf(district.State);
        if (UsStates.HouseOfficeByNonStateCode.TryGetValue(district.State, out var office)) return $"{office} for {state}";
        return district.District == 0 ? $"Representative for {state}, at large" : $"Representative for {state}, district {district.District}";
    }

    private static ZipMemberIdentityData? Member(SeatHolder? holder) => holder is null ? null
        : new(holder.OfficialId, holder.Name, holder.Party, holder.TermEndsOn, holder.PhotoPath,
            [.. holder.MissedVotes.Select(figure => new ZipMemberAttendanceData(figure.Chamber, figure.Congress, figure.Missed, figure.Held,
                figure.FirstVotedOn, figure.LastVotedOn, figure.Scope))],
            [.. holder.PartyLines.Select(figure => new ZipMemberPartyLineData(figure.Chamber, figure.Congress,
                figure.Party, figure.WithParty, figure.Cast, figure.FirstVotedOn, figure.LastVotedOn, figure.Scope))]);
}
