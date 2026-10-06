namespace CivicLens.Infrastructure.Collection.Persistence;

internal sealed class CaptureRow
{
    public string Sha256 { get; set; } = null!;
    public long ByteLength { get; set; }
}
