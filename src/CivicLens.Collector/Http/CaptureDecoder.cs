using System.IO.Compression;

namespace CivicLens.Collector.Http;

internal static class CaptureDecoder
{
    private const int CopyBufferSize = 81920;

    public static async Task<MemoryStream> ReadDecodedAsync(string capturePath, IReadOnlyList<string> encodings,
        long maximumBytes, CancellationToken token)
    {
        await using var file = File.OpenRead(capturePath);
        await using var storage = new GZipStream(file, CompressionMode.Decompress);
        Stream decoded = storage;
        var owned = new List<Stream>();
        try
        {
            foreach (var encoding in encodings.Reverse())
            {
                token.ThrowIfCancellationRequested();
                decoded = encoding.ToLowerInvariant() switch
                {
                    "identity" => decoded,
                    "gzip" => Own(new GZipStream(decoded, CompressionMode.Decompress), owned),
                    "deflate" => Own(new ZLibStream(decoded, CompressionMode.Decompress), owned),
                    "br" => Own(new BrotliStream(decoded, CompressionMode.Decompress), owned),
                    _ => throw new UnsupportedEncodingException()
                };
                decoded = Own(new BoundedReadStream(decoded, maximumBytes), owned);
            }
            using var bounded = new BoundedReadStream(decoded, maximumBytes);
            var output = new MemoryStream();
            await bounded.CopyToAsync(output, CopyBufferSize, token);
            // An inner decoder may stop before an outer decoder checks its trailer.
            foreach (var stage in owned.OfType<BoundedReadStream>().Reverse())
                await stage.CopyToAsync(Stream.Null, CopyBufferSize, token);
            return output;
        }
        finally
        {
            foreach (var stream in owned.AsEnumerable().Reverse()) await stream.DisposeAsync();
        }
    }

    private static Stream Own(Stream stream, List<Stream> owned)
    {
        owned.Add(stream);
        return stream;
    }
}

internal sealed class UnsupportedEncodingException : Exception;

internal sealed class BoundedStreamLimitException : Exception;

internal sealed class BoundedReadStream(Stream inner, long maximum) : Stream
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
    private int Check(int count) { read += count; if (read > maximum) throw new BoundedStreamLimitException(); return count; }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { base.Dispose(disposing); }
}
