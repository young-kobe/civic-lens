namespace CivicLens.Host.Publication;

public sealed record GlossaryGroup(string Title, IReadOnlyList<GlossaryTerm> Terms);
