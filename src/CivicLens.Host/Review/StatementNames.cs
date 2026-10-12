using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;

namespace CivicLens.Host.Review;

internal sealed class StatementNames(IEnumerable<AnalysisOfficial> officials, IssueConfiguration issues)
{
    private readonly Dictionary<string, string> officialNames = officials.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
    private readonly Dictionary<string, string> issueNames = issues.Issues.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
    private readonly Dictionary<string, string> policyAreaNames = issues.PolicyAreas.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);

    public string Official(string id) => officialNames.GetValueOrDefault(id, id);

    public string Issue(string id) => issueNames.GetValueOrDefault(id, id);

    public string PolicyArea(string id) => policyAreaNames.GetValueOrDefault(id, id);
}
