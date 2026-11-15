using System.Text.Json.Serialization;

namespace CivicLens.Application.Roster;

internal static class RosterWire
{
    public static string? Capped(string? text, int maximumLength)
    {
        var present = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return present is not null && present.Length > maximumLength
            ? throw new InvalidDataException("A roster value is longer than its stored limit.")
            : present;
    }

    public static string FullNameOf(WireName? name) =>
        Capped(name?.OfficialFull, RosterLimits.FullName) ?? Capped($"{name?.First} {name?.Last}", RosterLimits.FullName)
        ?? throw new InvalidDataException("Every roster entry must have a name.");

    /// <summary>The roster last name, kept whole, so a compound surname such as Van Hollen sorts as the source spells it. Null when the roster gives none.</summary>
    public static string? LastNameOf(WireName? name) => Capped(name?.Last, RosterLimits.FullName);

    public sealed record WireName(string? First, string? Last, [property: JsonPropertyName("official_full")] string? OfficialFull);
}
