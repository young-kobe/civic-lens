namespace CivicLens.Host.Components.Evidence;

public sealed record Fact(string Label, string Value, IReadOnlyList<FactLink>? Links = null)
{
    public static Fact List(string label, IEnumerable<string> values) =>
        new(label, values.Any() ? string.Join(", ", values) : "None");

    public static Fact LinkList(string label, IReadOnlyList<FactLink> links) =>
        links.Count == 0 ? List(label, []) : new(label, string.Join(", ", links.Select(link => link.Text)), links);
}
