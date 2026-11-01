namespace CivicLens.Host.Publication;

public sealed record GlossaryGroup(string Title, IReadOnlyList<GlossaryTerm> Terms)
{
    public string AnchorId => AnchorOf(Title);

    public static string AnchorOf(string title) => "glossary-group-" + title.ToLowerInvariant().Replace(' ', '-');
}
