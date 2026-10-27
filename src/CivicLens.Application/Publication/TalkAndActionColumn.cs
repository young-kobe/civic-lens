using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

/// <summary>The records of one column of the profile. A record with several issues counts once for each issue.</summary>
public sealed record TalkAndActionColumn(int WithIssue, int WithoutIssue, ImmutableDictionary<string, int> ByIssue)
{
    public int CountOf(string issueId) => ByIssue.GetValueOrDefault(issueId);

    public double? ShareOf(string issueId) => TalkAndActionProfile.ShareOf(CountOf(issueId), WithIssue);

    public static TalkAndActionColumn Of(IEnumerable<string[]> issueIdsOfRecords)
    {
        var records = issueIdsOfRecords.ToArray();
        return new(records.Count(issueIds => issueIds.Length > 0), records.Count(issueIds => issueIds.Length == 0),
            records.SelectMany(issueIds => issueIds).GroupBy(issueId => issueId, StringComparer.Ordinal)
                .ToImmutableDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
    }
}
