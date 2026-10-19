using CivicLens.Application.Issues;

namespace CivicLens.Host.Statements;

internal static class StatementIssueRubric
{
    public static IssueConfigurationRevision? FromEnvironment() =>
        ConfigurationFile.ReadIssuesFromEnvironment() is { } configuration ? IssueConfigurationRevision.Create(configuration) : null;
}
