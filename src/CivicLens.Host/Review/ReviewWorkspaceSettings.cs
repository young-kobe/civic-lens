using CivicLens.Core.Review;

namespace CivicLens.Host.Review;

public sealed record ReviewWorkspaceSettings(
    Uri Origin, Uri Authority, string ClientId, string ClientSecret, string OwnerSubject,
    IReadOnlySet<string> ReviewerSubjects, string KeyDirectory)
{
    public static ReviewWorkspaceSettings FromEnvironment()
    {
        var origin = ReadHttpsOrigin("CIVIC_LENS_REVIEW_ORIGIN");
        var authority = ReadHttpsOrigin("CIVIC_LENS_AUTH0_AUTHORITY");
        var owner = Required("CIVIC_LENS_REVIEW_OWNER");
        var reviewers = (Environment.GetEnvironmentVariable("CIVIC_LENS_REVIEW_REVIEWERS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        ValidateSubjects(owner, reviewers);
        var directory = Required("CIVIC_LENS_REVIEW_KEY_DIRECTORY");
        if (!Path.IsPathFullyQualified(directory))
            throw new ArgumentException("CIVIC_LENS_REVIEW_KEY_DIRECTORY must be an absolute persistent path.");
        return new(origin, authority, Required("CIVIC_LENS_AUTH0_CLIENT_ID"),
            Required("CIVIC_LENS_AUTH0_CLIENT_SECRET"), owner, reviewers, directory);
    }

    public static void ValidateSubjects(string owner, IReadOnlySet<string> reviewers)
    {
        if (owner.Length > 256 || reviewers.Count > 20 || reviewers.Any(value => value.Length > 256))
            throw new ArgumentException("Review subject configuration exceeds its limits.");
        if (ReviewAuthor.IsAnalysis(owner) || reviewers.Any(ReviewAuthor.IsAnalysis))
            throw new ArgumentException("The AI drafter's subject cannot be an owner or a reviewer.");
    }

    private static Uri ReadHttpsOrigin(string name)
    {
        if (!Uri.TryCreate(Required(name), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException($"{name} must be an HTTPS origin without a path, query, or credentials.");
        return uri;
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.");
        return value;
    }
}
