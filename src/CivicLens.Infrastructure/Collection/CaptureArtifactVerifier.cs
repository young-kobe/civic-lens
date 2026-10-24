using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;

namespace CivicLens.Infrastructure.Collection;

public sealed class CaptureArtifactVerifier : ICaptureArtifactVerifier
{
    public Task VerifyAsync(CollectionRequest request, CollectionResult receipt, string artifactRoot,
        CancellationToken cancellationToken)
    {
        request.Validate();
        receipt.ValidateAgainst(request);
        if (string.IsNullOrWhiteSpace(artifactRoot)) throw new ArgumentException("Artifact root is required.", nameof(artifactRoot));
        if (receipt.Outcome != CollectionOutcome.Captured) return Task.CompletedTask;
        return CollectorProcess.VerifyCaptureAsync(request, receipt, artifactRoot, cancellationToken);
    }
}
