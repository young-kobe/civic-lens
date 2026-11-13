using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Issues;

/// <summary>An issue of the active release, as a filter lists it.</summary>
public sealed record IssueOption(string IssueId, string Name)
{
    public SearchChoiceData ToChoice() => new(IssueId, Name);
}
