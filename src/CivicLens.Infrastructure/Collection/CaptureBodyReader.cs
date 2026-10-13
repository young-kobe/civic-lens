using System.IO.Compression;
using System.Security.Cryptography;
using CivicLens.Application.Collection;
using CivicLens.Core.Collection;

namespace CivicLens.Infrastructure.Collection;

public sealed class CaptureBodyReader : ICaptureBodyReader
{
    private const int MaximumInputBytes = 10_000_000;

    public async Task<byte[]> ReadAsync(CapturedAttemptResult attempt, string artifactRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        var path = Path.Combine(artifactRoot, attempt.Capture.Sha256 + ".gz");
        var entity = await ReadAndVerifyCaptureAsync(path, attempt.Capture, cancellationToken);
        return await DecodeContentAsync(entity, attempt.Response.ContentEncodings, cancellationToken);
    }

    private static async Task<byte[]> ReadAndVerifyCaptureAsync(string path, CaptureIdentity identity,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Capture artifact was not found.", path);
        if (info.Length > MaximumInputBytes) throw new InvalidDataException("Stored capture exceeds the input limit.");
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var bounded = new BoundedReadStream(gzip, MaximumInputBytes);
        using var output = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await bounded.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            length += count;
            hash.AppendData(buffer, 0, count);
            output.Write(buffer, 0, count);
        }
        if (length != identity.ByteLength || !CryptographicOperations.FixedTimeEquals(
                hash.GetHashAndReset(), Convert.FromHexString(identity.Sha256)))
            throw new InvalidDataException("Stored capture does not match its immutable identity.");
        return output.ToArray();
    }

    private static async Task<byte[]> DecodeContentAsync(byte[] bytes, IReadOnlyList<string> encodings,
        CancellationToken cancellationToken)
    {
        foreach (var encoding in encodings.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = encoding.Trim().ToLowerInvariant();
            if (normalized == "identity") continue;
            using var input = new MemoryStream(bytes, writable: false);
            using Stream decoder = normalized switch
            {
                "gzip" => new GZipStream(input, CompressionMode.Decompress),
                "deflate" => new ZLibStream(input, CompressionMode.Decompress),
                "br" => new BrotliStream(input, CompressionMode.Decompress),
                _ => throw new NotSupportedException("Content encoding is not supported.")
            };
            // Finish each encoding layer before starting the next so its trailer is validated.
            using var bounded = new BoundedReadStream(decoder, MaximumInputBytes);
            using var output = new MemoryStream();
            await bounded.CopyToAsync(output, 81920, cancellationToken);
            bytes = output.ToArray();
        }
        return bytes;
    }

    private sealed class BoundedReadStream(Stream inner, long maximum) : Stream
    {
        private long read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Check(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Check(inner.Read(buffer));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Check(await inner.ReadAsync(buffer, cancellationToken));
        private int Check(int count) { read += count; if (read > maximum) throw new InvalidDataException("Decoded input exceeds the limit."); return count; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
