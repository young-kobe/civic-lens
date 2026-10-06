using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

/// <summary>A fresh application-owned identity and bounded request, prepared before collection starts.</summary>
public sealed class PreparedCollectionAttempt
{
    public string AttemptId { get; }
    public CollectionRequest Request { get; }

    public PreparedCollectionAttempt(CollectionConfiguration configuration, string sourceId, string artifactDirectory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Request = configuration.CreateRequest(sourceId, Guid.NewGuid().ToString("N"), Path.GetFullPath(artifactDirectory));
        AttemptId = Guid.NewGuid().ToString("N");
    }
}
