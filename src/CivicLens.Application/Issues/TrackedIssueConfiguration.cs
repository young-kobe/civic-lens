using CivicLens.Core.Registry;

namespace CivicLens.Application.Issues;

public sealed record TrackedIssueConfiguration
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Definition { get; init; }
    public required string[] PolicyAreaIds { get; init; }
    public string[]? Includes { get; init; }
    public string[]? Excludes { get; init; }
    public DateOnly? StartsOn { get; init; }
    public DateOnly? EndsBefore { get; init; }

    internal void Validate(IReadOnlySet<string> policyAreaIds)
    {
        IssueConfiguration.ValidateId(Id, "Issue");
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Definition))
            throw new ArgumentException($"Issue '{Id}' must have a nonblank name and definition.");
        IssueConfiguration.ValidateNameLength(Name, $"Issue '{Id}'");
        ValidatePolicyAreas(policyAreaIds);
        ValidateTerms(Includes, "includes");
        ValidateTerms(Excludes, "excludes");
        _ = new DateRange(StartsOn, EndsBefore);
    }

    private void ValidatePolicyAreas(IReadOnlySet<string> knownIds)
    {
        if (PolicyAreaIds is null || PolicyAreaIds.Length == 0)
            throw new ArgumentException($"Issue '{Id}' must link to at least one policy area.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var policyAreaId in PolicyAreaIds)
        {
            if (policyAreaId is null || !knownIds.Contains(policyAreaId))
                throw new ArgumentException($"Issue '{Id}' references an unknown policy area '{policyAreaId}'.");
            if (!seen.Add(policyAreaId))
                throw new ArgumentException($"Issue '{Id}' repeats policy area '{policyAreaId}'.");
        }
    }

    private void ValidateTerms(string[]? terms, string listName)
    {
        if (terms is null) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term))
                throw new ArgumentException($"Issue '{Id}' {listName} entries must be nonblank.");
            if (!seen.Add(term))
                throw new ArgumentException($"Issue '{Id}' repeats {listName} entry '{term}'.");
        }
    }
}
