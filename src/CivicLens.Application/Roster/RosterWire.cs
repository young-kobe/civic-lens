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

    public sealed record WireName(string? First, string? Last, [property: JsonPropertyName("official_full")] string? OfficialFull);
}
