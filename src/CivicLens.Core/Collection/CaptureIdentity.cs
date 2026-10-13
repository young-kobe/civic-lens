namespace CivicLens.Core.Collection;

/// <summary>Identifies the captured entity bytes, independent of their storage path.</summary>
public sealed record CaptureIdentity
{
    public CaptureIdentity(string sha256, long byteLength)
    {
        if (!LowercaseHex.Is(sha256, 64))
            throw new ArgumentException("SHA-256 must be 64 lowercase hexadecimal characters.", nameof(sha256));
        if (byteLength < 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        Sha256 = sha256;
        ByteLength = byteLength;
    }

    public string Sha256 { get; }
    public long ByteLength { get; }
}
