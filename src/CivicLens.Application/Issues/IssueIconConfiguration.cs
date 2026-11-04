using System.Text.Json.Serialization;

namespace CivicLens.Application.Issues;

public sealed record IssueIconConfiguration
{
    public static IssueIconConfiguration None { get; } = new() { Icons = [] };

    [JsonRequired]
    public int Version { get; init; } = 1;
    public required Dictionary<string, string> Icons { get; init; }

    public void Validate(IssueConfiguration issues)
    {
        if (Version != 1)
            throw new ArgumentException("Unsupported issue icon configuration version.");
        if (Icons is null)
            throw new ArgumentException("Icons are required.");
        var issueIds = issues.Issues.Select(issue => issue.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (issueId, icon) in Icons)
        {
            if (!issueIds.Contains(issueId))
                throw new ArgumentException($"Icon file names unknown issue '{issueId}'.");
            if (icon is null || !IssueIcons.IsKnown(icon))
                throw new ArgumentException($"Issue '{issueId}' names an unknown icon '{icon}'.");
        }
    }
}
