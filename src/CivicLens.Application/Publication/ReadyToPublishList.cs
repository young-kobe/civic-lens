using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

public sealed record ReadyToPublishList(ImmutableArray<ReadyToPublishDraft> Items, bool HasMore);
