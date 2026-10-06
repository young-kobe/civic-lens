using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

public interface ICaptureArtifactVerifier
{
    Task VerifyAsync(CollectionRequest request, CollectionResult receipt, string artifactRoot,
        CancellationToken cancellationToken);
}
