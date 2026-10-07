using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Discovery;

public sealed record DiscoveryAdmissionRequest(
    string AttemptId,
    string IdempotencyKey,
    CollectionJobDefinition ArticleTemplate,
    DiscoveryAdmissionPolicy Policy,
    CollectionMode ExpectedDiscoveryMode = CollectionMode.Feed)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AttemptId) || AttemptId.Length > 128)
            throw new ArgumentException("Discovery attempt ID must contain 1 to 128 characters.", nameof(AttemptId));
        if (string.IsNullOrWhiteSpace(IdempotencyKey) || IdempotencyKey.Length > 256)
            throw new ArgumentException("Admission idempotency key must contain 1 to 256 characters.", nameof(IdempotencyKey));
        ArgumentNullException.ThrowIfNull(ArticleTemplate);
        ArgumentNullException.ThrowIfNull(Policy);
        Policy.Validate();
        ArticleTemplate.Validate();
        if (ExpectedDiscoveryMode is not (CollectionMode.Feed or CollectionMode.Html))
            throw new ArgumentOutOfRangeException(nameof(ExpectedDiscoveryMode), "Admission expects a discovery source mode.");
        if (ArticleTemplate.Mode != CivicLens.Collection.Contracts.CollectionMode.Page ||
            ArticleTemplate.ETag is not null || ArticleTemplate.LastModified is not null)
            throw new ArgumentException("Article template must be a Page job without conditional validators.", nameof(ArticleTemplate));
    }
}
