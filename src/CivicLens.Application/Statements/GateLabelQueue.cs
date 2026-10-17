using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public sealed record GateLabelQueue(ImmutableArray<GateLabelQueueItem> Items, int UnlabeledCount);
