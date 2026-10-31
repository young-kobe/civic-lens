using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

public sealed record PublishReleaseRequest(ImmutableArray<PublishedRecordReference> Records, string IdempotencyKey)
{
    public ImmutableArray<RecordNote> Withdrawals { get; init; } = [];
    public bool RewriteOfficials { get; init; }
    public bool RewriteEntries { get; init; }
    public bool StatusOnly { get; init; }
}
