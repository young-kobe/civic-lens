using CivicLens.PublicWeb.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Places published seat identities without inventing a slot or a vacancy for missing evidence.</summary>
public sealed class MapsDrawing
{
    public IReadOnlyList<MapFrameSeat> Seats { get; }
    public IReadOnlyList<VoteMapStateShape> SplitStates { get; }
    public VoteMapTileLayout Tiles { get; }
    public VoteMapGeometry? Geometry { get; }

    public MapsDrawing(MapsPageData data, string rootPath)
    {
        var members = data.Members.ToDictionary(member => member.OfficialId, StringComparer.Ordinal);
        var groups = data.Seats.GroupBy(seat => seat.State).OrderBy(group => UsStates.NameOf(group.Key), StringComparer.Ordinal)
            .Select(group => group.OrderBy(seat => seat.District).ThenBy(seat => seat.SenateClass).ToArray()).ToArray();
        Tiles = VoteMapTiles.Place([.. groups.Select(group => (group[0].State, group.Length))]);
        var senate = data.Chamber == PublishedChamber.Senate;
        Geometry = senate ? VoteMapGeometry.Senate : VoteMapGeometry.ForHouseCongress(data.Congress);
        var stateShapes = senate ? Geometry?.States : null;
        var districtShapes = senate ? null : Geometry?.Districts;
        var drawings = new List<MapFrameSeat>();
        var splits = new List<VoteMapStateShape>();
        var tileIndex = 0;

        foreach (var group in groups)
        {
            var state = group[0].State;
            var split = group.Length == 2 ? stateShapes?.GetValueOrDefault(state) : null;
            if (split is not null) splits.Add(split);
            for (var index = 0; index < group.Length; index++)
            {
                var seat = group[index];
                var member = seat.OfficialId is { } id ? members.GetValueOrDefault(id) : null;
                string? path = null;
                var drawShape = split is not null;
                if (districtShapes is not null && seat.District is { } district)
                    drawShape = districtShapes.TryGetValue((state, district), out path);

                var (mark, opacity) = Appearance(data, member);
                drawings.Add(new(state, path, index, Tiles.Tiles[tileIndex++], mark,
                    member?.Party is { } party ? VoteMapMarks.Name(PartyTones.Of(party)) : null,
                    member is null ? MapsWords.Unknown : MapsWords.Detail(data, member),
                    Href: member is null ? null : rootPath + PublicationProtocol.OfficialPagePath(Uri.EscapeDataString(member.OfficialId)),
                    DrawShape: drawShape, FillOpacity: opacity));
            }
        }

        // A single known Senate class has a tile, but cannot be assigned a geographic half.
        if (stateShapes is not null)
        {
            var splitNames = splits.Select(state => state.State).ToHashSet(StringComparer.Ordinal);
            drawings.AddRange(stateShapes.Values.Where(state => !splitNames.Contains(state.State))
                .Select(state => new MapFrameSeat(state.State, state.Path, 0, null, "unavailable", null, MapsWords.Unknown)));
        }
        Seats = drawings;
        SplitStates = splits;
    }

    private static (string Mark, double? Opacity) Appearance(MapsPageData data, MapMemberData? member)
    {
        if (member is null) return ("unavailable", null);
        if (data.Layer == "termEnds") return member.TermEndsOn is null ? ("unavailable", null)
            : member.TermEndsOn == data.SelectedEnd ? ("term-selected", null) : ("term-other", null);
        return MapsWords.Fraction(data, member) switch
        {
            { Base: 0 } => ("empty-base", null),
            { Share: { } share } => ("measure", .15 + .85 * share),
            _ => ("unavailable", null)
        };
    }
}
