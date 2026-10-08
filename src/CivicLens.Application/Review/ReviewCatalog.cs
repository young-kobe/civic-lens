using System.Collections.Immutable;

namespace CivicLens.Application.Review;

/// <summary>Configured values that may be attached to a public document-change record.</summary>
public sealed class ReviewCatalog
{
    public ReviewCatalog(IEnumerable<string> issueIds, IEnumerable<string> officialIds)
    {
        ArgumentNullException.ThrowIfNull(issueIds);
        ArgumentNullException.ThrowIfNull(officialIds);
        IssueIds = Validate(issueIds, nameof(issueIds));
        OfficialIds = Validate(officialIds, nameof(officialIds));
    }

    public ImmutableHashSet<string> IssueIds { get; }
    public ImmutableHashSet<string> OfficialIds { get; }

    private static ImmutableHashSet<string> Validate(IEnumerable<string> values, string name)
    {
        var result = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                throw new ArgumentException("Configured identifiers must contain 1 to 128 characters.", name);
            if (!result.Add(value)) throw new ArgumentException("Configured identifiers must be unique.", name);
            if (result.Count > 256) throw new ArgumentException("At most 256 identifiers may be configured.", name);
        }
        return result.ToImmutable();
    }
}
