namespace CivicLens.Host.Components.Evidence;

public sealed record Fact(string Label, string Value)
{
    public static Fact List(string label, IEnumerable<string> values) =>
        new(label, values.Any() ? string.Join(", ", values) : "None");
}
