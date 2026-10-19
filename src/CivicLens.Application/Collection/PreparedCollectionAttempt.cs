using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

/// <summary>A bounded request, prepared before collection starts.</summary>
public sealed class PreparedCollectionAttempt
{
    public CollectionRequest Request { get; }

    public PreparedCollectionAttempt(ConfiguredCollectionSource source, string artifactDirectory)
    {
        ArgumentNullException.ThrowIfNull(source);
        Request = source.CreateRequest(Guid.NewGuid().ToString("N"), Path.GetFullPath(artifactDirectory),
            DateOnly.FromDateTime(DateTime.UtcNow));
    }
}
