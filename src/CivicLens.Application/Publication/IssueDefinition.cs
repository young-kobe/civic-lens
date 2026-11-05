using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

/// <summary>The text that an issue page shows under "What this issue covers".</summary>
public sealed record IssueDefinition(string Text, ImmutableArray<string> Includes, ImmutableArray<string> Excludes);
