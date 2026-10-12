using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Issues;

public sealed partial record IssueConfiguration
{
    public const int MaximumIssues = 256;
    private const int MaximumIdLength = 128;

    [JsonRequired]
    public int Version { get; init; } = 1;
    public required PolicyAreaConfiguration[] PolicyAreas { get; init; }
    public required TrackedIssueConfiguration[] Issues { get; init; }

    public void Validate()
    {
        if (Version != 1)
            throw new ArgumentException("Unsupported issue configuration version.");
        if (PolicyAreas is null || Issues is null)
            throw new ArgumentException("Policy areas and issues are required.");
        if (Issues.Length > MaximumIssues)
            throw new ArgumentException($"At most {MaximumIssues} issues may be configured.");

        var policyAreaIds = ValidatePolicyAreas();
        var issueIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var issue in Issues)
        {
            if (issue is null)
                throw new ArgumentException("Issues cannot contain null entries.");
            issue.Validate(policyAreaIds);
            if (!issueIds.Add(issue.Id))
                throw new ArgumentException($"Duplicate issue ID '{issue.Id}'.");
        }
    }

    internal static void ValidateId(string? id, string kind)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaximumIdLength || !IdPattern().IsMatch(id))
            throw new ArgumentException($"{kind} ID '{id}' must be lowercase words joined by hyphens, at most {MaximumIdLength} characters.");
    }

    private HashSet<string> ValidatePolicyAreas()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in PolicyAreas)
        {
            if (area is null)
                throw new ArgumentException("Policy areas cannot contain null entries.");
            ValidateId(area.Id, "Policy area");
            if (string.IsNullOrWhiteSpace(area.Name))
                throw new ArgumentException($"Policy area '{area.Id}' must have a nonblank name.");
            if (!ids.Add(area.Id))
                throw new ArgumentException($"Duplicate policy area ID '{area.Id}'.");
            if (!names.Add(area.Name))
                throw new ArgumentException($"Duplicate policy area name '{area.Name}'.");
        }
        return ids;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();
}
