using System.Collections.Immutable;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipDraftContext(RelationshipDraftEvidence Evidence, RelationshipPair Pair, string Fingerprint,
    ImmutableArray<string> ListedNames, ImmutableArray<string> ExcludedNames)
{
    public static RelationshipDraftContext Create(RelationshipDraftEvidence evidence, RelationshipPair pair,
        IReadOnlyDictionary<string, string> configuredOfficials)
    {
        var listedIds = pair.Statement.Revision.SpeakerOfficialIds.Add(pair.OfficialId).ToHashSet(StringComparer.Ordinal);
        var listed = configuredOfficials.Where(person => listedIds.Contains(person.Key)).Select(person => person.Value).Distinct(StringComparer.Ordinal);
        var excluded = configuredOfficials.Where(person => !listedIds.Contains(person.Key)).Select(person => person.Value).Distinct(StringComparer.Ordinal);
        return new(evidence, pair, RelationshipFingerprint.Compute(pair), [.. listed], [.. excluded]);
    }
}
