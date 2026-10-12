using CivicLens.Application.Issues;
using CivicLens.Host.Review;

namespace CivicLens.Host.Statements;

internal static class StatementIssueRubric
{
    public static IssueConfigurationRevision? FromEnvironment() =>
        Environment.GetEnvironmentVariable("CIVIC_LENS_ISSUES_CONFIG") is null
            ? null
            : IssueConfigurationRevision.Create(ReviewIssueSettings.FromEnvironment().Configuration);
}
