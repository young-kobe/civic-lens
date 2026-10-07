using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using CivicLens.Collection.Contracts;

namespace CivicLens.Collector.Http;

internal static class FeedParser
{
    private const long MaximumDecodedCharacters = 10_000_000;
    private const int MaximumNodes = 100_000;
    private const int MaximumDepth = 64;

    public static async Task<FeedDiscoveryResult> ParseCaptureAsync(string capturePath, CollectionRequest request,
        string responseUrl, IReadOnlyList<string> encodings, CancellationToken token)
    {
        try
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
                }
                using var bounded = new BoundedReadStream(decoded, MaximumDecodedCharacters);
                using var xml = new MemoryStream();
                await bounded.CopyToAsync(xml, 81920, token);
                xml.Position = 0;
                using (var limitReader = XmlReader.Create(xml, Settings()))
                {
                    var nodes = 0;
                    while (await limitReader.ReadAsync())
                    {
                        token.ThrowIfCancellationRequested();
                        if (++nodes > MaximumNodes || limitReader.Depth > MaximumDepth)
                            return Result(FeedDiscoveryStatus.LimitExceeded);
                    }
                }
                xml.Position = 0;
                using var reader = XmlReader.Create(xml, Settings());
                var document = await XDocument.LoadAsync(reader, LoadOptions.None, token);
                var root = document.Root;
                if (root is null) return Result(FeedDiscoveryStatus.Invalid);
                if (root.DescendantsAndSelf().Attributes().Any(attribute => attribute.Name == XNamespace.Xml + "base"))
                    return Result(FeedDiscoveryStatus.Unsupported);
                var links = root.Name.LocalName switch
                {
                    "rss" when root.Name.NamespaceName.Length == 0 && (string?)root.Attribute("version") == "2.0" => ParseRss(root),
                    "feed" when root.Name == XName.Get("feed", "http://www.w3.org/2005/Atom") => ParseAtom(root),
                    _ => throw new UnsupportedFeedException()
                };
                var urls = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var link in links)
                {
                    token.ThrowIfCancellationRequested();
                    if (link.Length > 4096 || !Uri.TryCreate(new Uri(responseUrl), link, out var resolved))
                        return Result(FeedDiscoveryStatus.Invalid);
                    if (!request.Allows(resolved)) continue;
                    var value = resolved.AbsoluteUri;
                    if (value.Length > 4096) return Result(FeedDiscoveryStatus.Invalid);
                    if (seen.Add(value)) urls.Add(value);
                    if (urls.Count > request.MaxCandidates) return Result(FeedDiscoveryStatus.LimitExceeded);
                }
                return new FeedDiscoveryResult { Status = FeedDiscoveryStatus.Parsed, Urls = urls.ToArray() };
            }
            finally
            {
                foreach (var stream in owned.AsEnumerable().Reverse()) await stream.DisposeAsync();
            }
        }
        catch (UnsupportedEncodingException) { return Result(FeedDiscoveryStatus.Unsupported); }
        catch (UnsupportedFeedException) { return Result(FeedDiscoveryStatus.Unsupported); }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or IOException or BoundedStreamLimitException)
        {
            return Result(exception is BoundedStreamLimitException ? FeedDiscoveryStatus.LimitExceeded : FeedDiscoveryStatus.Invalid);
        }

        static Stream Own(Stream stream, List<Stream> owned) { owned.Add(stream); return stream; }
        static FeedDiscoveryResult Result(FeedDiscoveryStatus status) => new() { Status = status, Urls = [] };
        static XmlReaderSettings Settings() => new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumDecodedCharacters,
            MaxCharactersFromEntities = 0,
            Async = true,
            IgnoreComments = true
        };
    }

    private static IEnumerable<string> ParseRss(XElement root)
    {
        foreach (var item in root.Elements("channel").Elements("item"))
            foreach (var link in item.Elements("link"))
                if (!string.IsNullOrWhiteSpace(link.Value)) yield return link.Value.Trim();
    }

    private static IEnumerable<string> ParseAtom(XElement root)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        foreach (var entry in root.Elements(atom + "entry"))
            foreach (var link in entry.Elements(atom + "link"))
            {
                var rel = (string?)link.Attribute("rel");
                if (string.IsNullOrEmpty(rel) || rel == "alternate")
                {
                    var href = (string?)link.Attribute("href");
                    if (!string.IsNullOrWhiteSpace(href)) yield return href.Trim();
                }
            }
    }

    private sealed class UnsupportedEncodingException : Exception;
    private sealed class UnsupportedFeedException : Exception;
    private sealed class BoundedStreamLimitException : Exception;

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
        private int Check(int count) { read += count; if (read > maximum) throw new BoundedStreamLimitException(); return count; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { base.Dispose(disposing); }
    }
}
