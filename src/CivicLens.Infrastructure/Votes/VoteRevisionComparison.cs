using System.Text.Json.Nodes;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Owns the comparison of a rebuilt vote revision with its stored JSON, as trees. It names the top-level keys that differ and never
/// returns a value, because the text can be private. The one allowed absence is the <c>source</c> key of a revision from a source that
/// never wrote it. Every other key must match, so an absent <c>issueCandidateIds</c> or <c>subjectsHash</c> equals a rebuilt revision that has none.</summary>
internal static class VoteRevisionComparison
{
    public const string SourceKey = "source";
    public const string RevisionJson = "revisionJson";

    private const string SourceIdKey = "sourceId";
    private const string SourceIdWithoutSourceKey = "congress-house-votes";

    /// <summary>Revisions of this source before the Senate had no source key. Every other revision of the source says CongressGov, so the source ID proves the value.</summary>
    public static bool MayOmitSource(JsonObject stored) =>
        stored[SourceIdKey] is JsonValue id && id.TryGetValue<string>(out var text) && text == SourceIdWithoutSourceKey;

    public static IReadOnlyList<string> DifferingKeys(string storedJson, VoteDraftRevision rebuilt)
    {
        JsonObject stored;
        try
        {
            stored = JsonNode.Parse(storedJson) as JsonObject ?? throw new InvalidDataException("A stored vote revision is no JSON object.");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return [RevisionJson];
        }
        var expected = ReviewJson.ToObject(rebuilt);
        var sourceMayBeAbsent = !stored.ContainsKey(SourceKey) && MayOmitSource(stored);
        return [.. stored.Select(property => property.Key).Union(expected.Select(property => property.Key), StringComparer.Ordinal)
            .Where(key => !(key == SourceKey && sourceMayBeAbsent) && !JsonNode.DeepEquals(stored[key], expected[key]))];
    }
}
