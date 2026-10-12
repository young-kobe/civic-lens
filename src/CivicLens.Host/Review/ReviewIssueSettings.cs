using System.Text.Json;
using CivicLens.Application.Issues;
using CivicLens.Collection.Contracts;

namespace CivicLens.Host.Review;

internal sealed record ReviewIssueSettings(IssueConfiguration Configuration)
{
    public static ReviewIssueSettings FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("CIVIC_LENS_ISSUES_CONFIG");
        if (path is null) return new(new IssueConfiguration { PolicyAreas = [], Issues = [] });
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Issue configuration must be an absolute path.");
        var file = new FileInfo(path);
        if (file.Length > 1_000_000) throw new ArgumentException("Issue configuration exceeds the workspace limit.");
        var configuration = JsonSerializer.Deserialize<IssueConfiguration>(File.ReadAllText(path), CollectionProtocol.JsonOptions)
            ?? throw new ArgumentException("Issue configuration is required.");
        configuration.Validate();
        return new(configuration);
    }
}
