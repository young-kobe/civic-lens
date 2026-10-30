using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

public sealed record PublishReleaseRequest(ImmutableArray<PublishedRecordReference> Records, string IdempotencyKey)
{
    public ImmutableArray<RecordNote> Notes { get; init; } = [];
    public ImmutableArray<RecordNote> Withdrawals { get; init; } = [];
    public bool RewriteOfficials { get; init; }
}
