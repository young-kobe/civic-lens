using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

public sealed record ReadyToPublishList(ImmutableArray<ReadyToPublishRecord> Items, bool HasMore);
