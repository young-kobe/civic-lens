using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Publication;

public sealed record PublishReleaseRequest(ImmutableArray<PublishedRecordReference> Records, string IdempotencyKey)
{
    public ImmutableArray<RecordNote> Withdrawals { get; init; } = [];
    public bool RewriteOfficials { get; init; }
    public bool RewriteEntries { get; init; }
    // The request hash is the idempotency payload. A default value stays out of it, so the hash of every earlier request holds.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StatusOnly { get; init; }
}
