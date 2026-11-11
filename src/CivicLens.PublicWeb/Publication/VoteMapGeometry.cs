using System.Globalization;
using System.Text.Json;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The one reader of the committed shape file <c>wwwroot/data/districts-119.json</c>. The map takes its shapes from this file only.
/// The assembly embeds the file, and the first map reads it once.
/// </summary>
public static class VoteMapGeometry
{
    public const string Asset = "assets/data/districts-119.json";

    private static readonly Lazy<Shapes> Loaded = new(Load);

    /// <summary>The Congress whose House districts the file draws.</summary>
    public static int Congress => Loaded.Value.Congress;

    public static string ViewBox => Loaded.Value.ViewBox;

    /// <summary>The shape of each House district with a shape, by state and district. District 0 is an at-large seat. Delegate areas have no shape and are not here.</summary>
    public static IReadOnlyDictionary<(string State, int District), string> Districts => Loaded.Value.Districts;

    /// <summary>The outline of each state and the box around it.</summary>
    public static IReadOnlyDictionary<string, VoteMapStateShape> States => Loaded.Value.States;

    private static Shapes Load()
    {
        var file = JsonSerializer.Deserialize<ShapeFile>(EmbeddedPublicAssets.Read(Asset), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"{Asset} is empty.");
        var districts = file.Districts.Where(district => !district.Delegate)
            .ToDictionary(district => (district.State, district.District), district => district.Path);
        var states = file.States.ToDictionary(state => state.State, state => Outline(state.State, state.Path));
        return new(file.Congress, file.ViewBox, districts, states);
    }

    /// <summary>The path holds only move and close commands, so every number pair after M is a point.</summary>
    private static VoteMapStateShape Outline(string state, string path)
    {
        var numbers = path.Split([' ', 'M', 'Z'], StringSplitOptions.RemoveEmptyEntries)
            .Select(number => double.Parse(number, CultureInfo.InvariantCulture)).ToArray();
        var xs = numbers.Where((_, index) => index % 2 == 0).ToArray();
        var ys = numbers.Where((_, index) => index % 2 == 1).ToArray();
        return new(state, path, xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private sealed record Shapes(int Congress, string ViewBox, IReadOnlyDictionary<(string, int), string> Districts, IReadOnlyDictionary<string, VoteMapStateShape> States);

    private sealed record ShapeFile(int Congress, string ViewBox, ShapeDistrict[] Districts, ShapeState[] States);

    private sealed record ShapeDistrict(string State, int District, bool Delegate, string Path);

    private sealed record ShapeState(string State, string Path);
}
