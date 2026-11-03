using System.Text.Json.Nodes;

namespace CivicLens.Infrastructure.Review;

/// <summary>Owns the comparison of a rebuilt revision with its stored JSON, as trees. It names the top-level keys that differ and never
/// returns a value, because the text can be private. A key in the allowed absences may be missing from the stored JSON.</summary>
internal static class RevisionJsonComparison
{
    public const string UnreadableKey = "revisionJson";

    /// <summary>Parses the stored JSON and reads every key at once, so a malformed object fails here and not later.</summary>
    public static JsonObject? TryParseObject(string json)
    {
        try
        {
            var stored = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("A stored revision is no JSON object.");
            _ = stored.Count;
            return stored;
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return null;
        }
    }

    public static IReadOnlyList<string> DifferingKeys(JsonObject stored, JsonObject expected, IReadOnlySet<string> allowedAbsences) =>
        [.. stored.Select(property => property.Key).Union(expected.Select(property => property.Key), StringComparer.Ordinal)
            .Where(key => !(allowedAbsences.Contains(key) && !stored.ContainsKey(key)) && Differs(stored, expected, key))];

    private static bool Differs(JsonObject stored, JsonObject expected, string key) =>
        stored.TryGetPropertyValue(key, out var actual) != expected.TryGetPropertyValue(key, out var wanted) || !JsonNode.DeepEquals(actual, wanted);
}
