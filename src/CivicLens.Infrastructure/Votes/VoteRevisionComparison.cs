using System.Text.Json.Nodes;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Owns the vote rules of the comparison of a rebuilt vote revision with its stored JSON. The one allowed absence is the <c>source</c> key of a revision from a source that
/// never wrote it. Every other key must match, so an absent <c>issueCandidateIds</c> or <c>subjectsHash</c> equals a rebuilt revision that has none.</summary>
internal static class VoteRevisionComparison
{
    public const string SourceKey = "source";

    private const string SourceIdKey = "sourceId";
    private const string SourceIdWithoutSourceKey = "congress-house-votes";

    /// <summary>Revisions of this source before the Senate had no source key. Every other revision of the source says CongressGov, so the source ID proves the value.</summary>
    public static bool MayOmitSource(JsonObject stored) =>
        stored[SourceIdKey] is JsonValue id && id.TryGetValue<string>(out var text) && text == SourceIdWithoutSourceKey;

    public static IReadOnlyList<string> DifferingKeys(string storedJson, VoteDraftRevision rebuilt)
    {
        if (RevisionJsonComparison.TryParseObject(storedJson) is not { } stored) return [RevisionJsonComparison.UnreadableKey];
        var sourceMayBeAbsent = !stored.ContainsKey(SourceKey) && MayOmitSource(stored) && rebuilt.Source == VoteSource.CongressGov;
        return RevisionJsonComparison.DifferingKeys(stored, ReviewJson.ToObject(rebuilt), sourceMayBeAbsent ? new HashSet<string> { SourceKey } : []);
    }
}
