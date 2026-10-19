using CivicLens.Application.Issues;

namespace CivicLens.Host.Statements;

internal static class StatementIssueRubric
{
    public static IssueConfigurationRevision? FromEnvironment() => From(ConfigurationFile.ReadIssuesFromEnvironment());

    public static IssueConfigurationRevision? From(IssueConfiguration? configuration) =>
        configuration is null ? null : IssueConfigurationRevision.Create(configuration);
}
