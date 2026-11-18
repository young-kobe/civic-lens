using System.Text.Json;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// Reads committed boundary files embedded in the assembly. Each file loads once, on first use.
/// </summary>
public sealed class VoteMapGeometry
{
    private static readonly Lazy<VoteMapGeometry> house118 = new(() => Load(118, 2023));
    private static readonly Lazy<VoteMapGeometry> house119 = new(() => Load(119, 2024));

    private readonly Lazy<IReadOnlyDictionary<string, VoteMapStateShape>> states;

    public int Congress { get; }
    public int SourceYear { get; }
    public string ViewBox { get; }

    /// <summary>House shapes by state and district. District 0 is an at-large seat. Delegate areas have no shape.</summary>
    public IReadOnlyDictionary<(string State, int District), string> Districts { get; }

    /// <summary>Senate maps use state outlines from the 119th Congress boundary file.</summary>
    public static VoteMapGeometry Senate => house119.Value;

    /// <summary>The outline of each state and the box around it.</summary>
    public IReadOnlyDictionary<string, VoteMapStateShape> States => states.Value;

    public static VoteMapGeometry? ForHouseCongress(int? congress) => congress switch
    {
        118 => house118.Value,
        119 => house119.Value,
        _ => null
    };

    private VoteMapGeometry(ShapeFile file, int sourceYear)
    {
        Congress = file.Congress;
        SourceYear = sourceYear;
        ViewBox = file.ViewBox;
        Districts = file.Districts.Where(district => !district.Delegate)
            .ToDictionary(district => (district.State, district.District), district => district.Path);
        var outlines = file.States;
        states = new(() => outlines.ToDictionary(state => state.State, state => Outline(state.State, state.Path)));
    }

    private static VoteMapGeometry Load(int congress, int sourceYear)
    {
        var asset = $"assets/data/districts-{congress}.json";
        var file = JsonSerializer.Deserialize<ShapeFile>(EmbeddedPublicAssets.Read(asset), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"{asset} is empty.");
        var source = $"https://www2.census.gov/geo/tiger/GENZ{sourceYear}/kml/cb_{sourceYear}_us_cd{congress}_20m.zip";
        if (file.Congress != congress || file.Source != source || string.IsNullOrWhiteSpace(file.ViewBox))
            throw new InvalidDataException($"{asset} has invalid boundary metadata for Congress {congress}.");
        return new(file, sourceYear);
    }

    private static VoteMapStateShape Outline(string state, string path)
    {
        var rings = VoteMapArea.Rings(path);
        var points = rings.SelectMany(ring => ring).ToArray();
        var (left, right) = (points.Min(point => point.X), points.Max(point => point.X));
        return new(state, path, left, points.Min(point => point.Y), right, points.Max(point => point.Y), VoteMapArea.HalvingX(rings, left, right));
    }

    private sealed record ShapeFile(int Congress, string Source, string ViewBox, ShapeDistrict[] Districts, ShapeState[] States);

    private sealed record ShapeDistrict(string State, int District, bool Delegate, string Path);

    private sealed record ShapeState(string State, string Path);
}
