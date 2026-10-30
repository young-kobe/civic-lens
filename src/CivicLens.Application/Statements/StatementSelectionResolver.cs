using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Issues;

namespace CivicLens.Application.Statements;

public sealed class StatementSelectionResolver(IssueConfiguration issues)
{
    private readonly StatementSpeakerRoster roster = new();
    private readonly ImmutableHashSet<string> issueIds = [.. issues.Issues.Select(issue => issue.Id)];
    private readonly ImmutableHashSet<string> policyAreaIds = [.. issues.PolicyAreas.Select(area => area.Id)];

    public StatementResolvedSelections Resolve(CollectionConfigurationRevision? revision, string sourceId, DateOnly capturedOn)
    {
        var speakers = roster.SpeakersOn(revision, sourceId, capturedOn);
        return new(new([.. speakers.Officials.Select(official => official.Id)], issueIds, policyAreaIds), speakers.Officials, speakers.OfficeOf);
    }
}

public sealed record StatementResolvedSelections(StatementAllowedSelections Allowed, ImmutableArray<AnalysisOfficial> Officials,
    string? OfficeOf);
