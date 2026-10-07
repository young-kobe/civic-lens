using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed record DocumentProfileConfiguration
{
    public required string Id { get; init; }
    public required string Selector { get; init; }
    public string[] ExcludedSelectors { get; init; } = [];

    public DocumentContentProfile ToProfile() => new(Id, Selector, ExcludedSelectors);
}
