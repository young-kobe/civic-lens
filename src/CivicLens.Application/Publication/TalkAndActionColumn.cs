using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

/// <summary>The count of the records of one column of the profile on each issue. A record with several issues counts once for each issue.</summary>
public sealed record TalkAndActionColumn(ImmutableDictionary<string, int> ByIssue)
{
    public int CountOf(string issueId) => ByIssue.GetValueOrDefault(issueId);

    public static TalkAndActionColumn Of(IEnumerable<string[]> issueIdsOfRecords) =>
        new(issueIdsOfRecords.SelectMany(issueIds => issueIds).GroupBy(issueId => issueId, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
}
