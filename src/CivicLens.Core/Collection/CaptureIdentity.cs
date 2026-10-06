namespace CivicLens.Core.Collection;

/// <summary>Identifies the captured entity bytes, independent of their storage path.</summary>
public sealed record CaptureIdentity
{
    public CaptureIdentity(string sha256, long byteLength)
    {
        if (sha256 is not { Length: 64 } || sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("SHA-256 must be 64 lowercase hexadecimal characters.", nameof(sha256));
        if (byteLength < 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        Sha256 = sha256;
        ByteLength = byteLength;
    }

    public string Sha256 { get; }
    public long ByteLength { get; }
}
