namespace CivicLens.Application.Publication;

/// <summary>One issue or policy area that at least one manifest entry of a release holds.</summary>
public sealed record ReleaseFacet(string Id, string Name, IssueDefinition? Definition);
