using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

public sealed record PublishReleaseRequest(ImmutableArray<PublishedRecordReference> Records, string IdempotencyKey);
