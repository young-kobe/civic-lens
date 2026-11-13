using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The vote map of one roll call: the seats in state name order, the House delegates, and the tile layout.
/// The delegates are House members of a non-state seat. They show as circles below the map and are not in the seat grid.
/// A member with no state in the release, or a House member with a state and no district, has no seat and counts as unplaced.
/// </summary>
public sealed record VoteMapData(
    PublishedChamber Chamber,
    IReadOnlyList<VoteMapSeat> Seats,
    IReadOnlyList<VoteMemberData> Delegates,
    IReadOnlyList<VoteMapStateShape> SplitStates,
    VoteMapTileLayout Tiles,
    int Unplaced)
{
    private const int SenateSeats = 2;

    /// <summary>Each mark in use on the seats and the delegate circles, with its count, in the order of <see cref="VoteMapMark"/>.</summary>
    public IReadOnlyList<(VoteMapMark Mark, int Count)> Legend =>
    [
        .. Seats.Select(seat => seat.Mark).Concat(Delegates.Select(VoteMapMarks.Of))
            .GroupBy(mark => mark).OrderBy(group => group.Key).Select(group => (group.Key, group.Count()))
    ];

    /// <summary>
    /// The parties of the members on the map, in the party order of every bar, each with its casts. A seat with no member is in no party.
    /// The casts of one party add up to the members of that party, and the groups add up to every seat and circle that has a member.
    /// </summary>
    public IReadOnlyList<VoteMapPartyGroup> PartyLegend =>
    [
        .. Seats.Select(seat => seat.Member).Concat(Delegates).OfType<VoteMemberData>()
            .GroupBy(member => member.Party)
            .Select(party => new VoteMapPartyGroup(party.Key, [.. party.GroupBy(VoteMapMarks.Of).OrderBy(cast => cast.Key).Select(cast => (cast.Key, cast.Count()))]))
            .OrderBy(group => group.Party is null ? int.MaxValue : PartyTones.Rank(group.Party)).ThenBy(group => group.Party, StringComparer.Ordinal)
    ];

    /// <summary>
    /// Joins the members of a vote to the seats of the shape file. Null when the map cannot show the vote truly:
    /// a House vote of another Congress than the shapes, a member whose seat has no shape, two members on one House seat,
    /// more than two senators in one state, or no member on any seat.
    /// </summary>
    public static VoteMapData? Of(VotePageData vote)
    {
        var placed = vote.Members.Where(member => member.State is not null).ToArray();
        var unplaced = vote.Members.Length - placed.Length;
        return vote.Chamber == PublishedChamber.Senate ? Senate(placed, unplaced) : House(vote.Congress, placed, unplaced);
    }

    private static VoteMapData? House(int congress, VoteMemberData[] placed, int unplaced)
    {
        if (congress != VoteMapGeometry.Congress) return null;
        var delegates = placed.Where(member => NonStateSeats.Contains(member.State!)).ToArray();
        var members = placed.Except(delegates).ToArray();
        unplaced += members.Count(member => member.District is null);
        var bySeat = members.Where(member => member.District is not null).GroupBy(member => (member.State!, member.District!.Value)).ToArray();
        if (bySeat.Any(seat => seat.Count() > 1 || !VoteMapGeometry.Districts.ContainsKey(seat.Key))) return null;
        if (bySeat.Length == 0 && delegates.Length == 0) return null;
        var holders = bySeat.ToDictionary(seat => seat.Key, seat => seat.Single());
        var districts = VoteMapGeometry.Districts.Keys.OrderBy(key => UsStates.NameOf(key.State), StringComparer.Ordinal).ThenBy(key => key.District).ToArray();
        var layout = VoteMapTiles.Place([.. districts.GroupBy(key => key.State).Select(state => (state.Key, state.Count()))]);
        var seats = districts.Select((key, index) => new VoteMapSeat(key.State, key.District, holders.GetValueOrDefault(key), layout.Tiles[index],
            VoteMapGeometry.Districts[key], 0)).ToArray();
        return new(PublishedChamber.House, seats, delegates, [], layout, unplaced);
    }

    /// <summary>Each state splits in two in the position order of the vote members, which is the order of <see cref="VotePageData.Members"/>.</summary>
    private static VoteMapData? Senate(VoteMemberData[] placed, int unplaced)
    {
        var byState = placed.GroupBy(member => member.State!).ToDictionary(state => state.Key, state => state.ToArray());
        if (byState.Count == 0 || byState.Any(state => state.Value.Length > SenateSeats || !VoteMapGeometry.States.ContainsKey(state.Key))) return null;
        var states = VoteMapGeometry.States.Values.OrderBy(state => UsStates.NameOf(state.State), StringComparer.Ordinal).ToArray();
        var layout = VoteMapTiles.Place([.. states.Select(state => (state.State, SenateSeats))]);
        var seats = states.SelectMany((state, index) => Enumerable.Range(0, SenateSeats).Select(half => new VoteMapSeat(state.State, null,
            byState.GetValueOrDefault(state.State)?.ElementAtOrDefault(half), layout.Tiles[index * SenateSeats + half], null, half))).ToArray();
        return new(PublishedChamber.Senate, seats, [], states, layout, unplaced);
    }
}
