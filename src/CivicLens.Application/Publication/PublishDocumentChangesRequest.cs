using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

public sealed record PublishDocumentChangesRequest(ImmutableArray<string> DraftIds, string IdempotencyKey);
