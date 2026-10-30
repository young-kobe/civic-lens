namespace CivicLens.Host.Publication;

public sealed record GlossaryTerm(string Key, string Name, string Means, string WhyItMatters, string Source, string? LongForm = null)
{
    public string AnchorId => "glossary-" + Key;

    public string PopoverId => "term-" + Key;
}
