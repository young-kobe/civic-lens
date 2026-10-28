using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

/// <summary>The records of one column of the profile on each issue. A record with several issues counts once for each issue.</summary>
public sealed record TalkAndActionColumn(ImmutableDictionary<string, ImmutableArray<string>> RecordIdsByIssue)
{
    public int CountOf(string issueId) => RecordIdsByIssue.TryGetValue(issueId, out var recordIds) ? recordIds.Length : 0;

    public IEnumerable<(string RecordId, string IssueId)> Counted() =>
        RecordIdsByIssue.SelectMany(issue => issue.Value.Select(recordId => (recordId, issue.Key)));

    public static TalkAndActionColumn Of(IEnumerable<(string RecordId, string[] IssueIds)> records) =>
        new(records.SelectMany(record => record.IssueIds.Select(issueId => (record.RecordId, issueId)))
            .GroupBy(pair => pair.issueId, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => group.Select(pair => pair.RecordId).ToImmutableArray(), StringComparer.Ordinal));
}
