using System.Collections.Immutable;
using CivicLens.Application.Issues;

namespace CivicLens.Application.Publication;

public sealed class PublicationCatalog(IReadOnlyDictionary<string, string> officialNames, IssueConfiguration issues)
{
    private readonly ImmutableDictionary<string, string> issueNames =
        issues.Issues.ToImmutableDictionary(issue => issue.Id, issue => issue.Name, StringComparer.Ordinal);
    private readonly ImmutableDictionary<string, string> policyAreaNames =
        issues.PolicyAreas.ToImmutableDictionary(area => area.Id, area => area.Name, StringComparer.Ordinal);

    public ImmutableDictionary<string, string> OfficialNames { get; } =
        officialNames.ToImmutableDictionary(StringComparer.Ordinal);

    public bool HasIssue(string id) => issueNames.ContainsKey(id);

    public string IssueName(string id) => issueNames.GetValueOrDefault(id, id);

    public string PolicyAreaName(string id) => policyAreaNames.GetValueOrDefault(id, id);
}
