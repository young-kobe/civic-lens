using System.Collections.Immutable;
using CivicLens.Application.Collection;

namespace CivicLens.Host.Review;

internal sealed record ReviewOfficials(ImmutableArray<KeyValuePair<string, string>> People)
{
    public ImmutableDictionary<string, string> Names { get; } = People.ToImmutableDictionary(StringComparer.Ordinal);

    public static ReviewOfficials From(CollectionConfiguration? configuration) => new(
        [.. (configuration?.People ?? []).OrderBy(person => person.Name, StringComparer.Ordinal)
            .Select(person => KeyValuePair.Create(person.Id, person.Name))]);
}
