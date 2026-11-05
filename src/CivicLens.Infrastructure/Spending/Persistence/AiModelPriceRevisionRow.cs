using CivicLens.Application.Spending;

namespace CivicLens.Infrastructure.Spending.Persistence;

internal sealed class AiModelPriceRevisionRow
{
    public string Model { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public long InputMicros { get; set; }
    public long OutputMicros { get; set; }
    public long CacheReadMicros { get; set; }
    public long CacheWriteMicros { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }

    public ModelPrice ToPrice() => new(Model, InputMicros, OutputMicros, CacheReadMicros, CacheWriteMicros);
}
