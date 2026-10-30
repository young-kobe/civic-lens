namespace CivicLens.Application.Issues;

public static class TrackedIssuePromptDocument
{
    public static object Of(TrackedIssueConfiguration issue) => new
    {
        id = issue.Id,
        name = issue.Name,
        definition = issue.Definition,
        policyAreaIds = issue.PolicyAreaIds,
        includes = issue.Includes,
        excludes = issue.Excludes
    };
}
