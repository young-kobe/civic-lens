using System.Collections.Immutable;

namespace CivicLens.Application.Briefs;

public sealed record BriefDraftContext(BriefDraftEvidence Evidence, BriefSelection Selection, string Fingerprint,
    ImmutableDictionary<string, string> ListedOfficials, ImmutableArray<string> ExcludedNames)
{
    public static BriefDraftContext Create(BriefDraftEvidence evidence, BriefSelection selection,
        IReadOnlyDictionary<string, string> configuredOfficials)
    {
        var sentIds = selection.Sent.SelectMany(item => item.Record.OfficialIds).ToHashSet(StringComparer.Ordinal);
        var listed = configuredOfficials.Where(person => sentIds.Contains(person.Key)).ToImmutableDictionary(StringComparer.Ordinal);
        var excluded = configuredOfficials.Where(person => !sentIds.Contains(person.Key)).Select(person => person.Value)
            .Distinct(StringComparer.Ordinal).ToImmutableArray();
        return new(evidence, selection with { Considered = selection.Considered + evidence.LeftOut }, BriefFingerprint.Compute(evidence.Window.CutoffReleaseNumber, selection.Sent), listed, excluded);
    }
}
