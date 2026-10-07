using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CivicLens.Collection.Contracts;

namespace CivicLens.Collector.Http;

internal static class FeedParser
{
    private const long MaximumDecodedCharacters = 10_000_000;
    private const int MaximumNodes = 100_000;
    private const int MaximumDepth = 64;

    public static async Task<DiscoveryResult> ParseCaptureAsync(string capturePath, CollectionRequest request,
        string responseUrl, IReadOnlyList<string> encodings, string? contentType, CancellationToken token)
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
                using (var limitReader = CreateXmlReader(xml, contentType))
                {
                    var nodes = 0;
                    while (await limitReader.ReadAsync())
                    {
                        token.ThrowIfCancellationRequested();
                        if (++nodes > MaximumNodes || limitReader.Depth > MaximumDepth)
                            return Result(DiscoveryStatus.LimitExceeded);
                    }
                }
                xml.Position = 0;
                using var reader = CreateXmlReader(xml, contentType);
                var document = await XDocument.LoadAsync(reader, LoadOptions.None, token);
                var root = document.Root;
                if (root is null) return Result(DiscoveryStatus.Invalid);
                if (root.DescendantsAndSelf().Attributes().Any(attribute => attribute.Name == XNamespace.Xml + "base"))
                    return Result(DiscoveryStatus.Unsupported);
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
                        return Result(DiscoveryStatus.Invalid);
                    if (!request.Allows(resolved)) continue;
                    var value = resolved.AbsoluteUri;
                    if (value.Length > 4096) return Result(DiscoveryStatus.Invalid);
                    if (seen.Add(value)) urls.Add(value);
                    if (urls.Count > request.MaxCandidates) return Result(DiscoveryStatus.LimitExceeded);
                }
                return new DiscoveryResult { Status = DiscoveryStatus.Parsed, Urls = urls.ToArray() };
            }
            finally
            {
                foreach (var stream in owned.AsEnumerable().Reverse()) await stream.DisposeAsync();
            }
        }
        catch (UnsupportedEncodingException) { return Result(DiscoveryStatus.Unsupported); }
        catch (UnsupportedFeedException) { return Result(DiscoveryStatus.Unsupported); }
        catch (Exception exception) when (exception is XmlException or DecoderFallbackException or InvalidDataException or IOException or BoundedStreamLimitException)
        {
            return Result(exception is BoundedStreamLimitException ? DiscoveryStatus.LimitExceeded : DiscoveryStatus.Invalid);
        }

        static Stream Own(Stream stream, List<Stream> owned) { owned.Add(stream); return stream; }
        static DiscoveryResult Result(DiscoveryStatus status) => new() { Status = status, Urls = [] };
    }

    private static XmlReader CreateXmlReader(MemoryStream input, string? contentType)
    {
        var (encoding, preambleLength) = ReadByteOrderMark(input.GetBuffer().AsSpan(0, (int)input.Length));
        if (encoding is null && contentType is not null)
        {
            var charset = MediaTypeHeaderValue.Parse(contentType).CharSet?.Trim('"');
            if (charset is not null)
            {
                try { encoding = Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                    throw new UnsupportedFeedException();
                }
            }
        }
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumDecodedCharacters,
            MaxCharactersFromEntities = 0,
            Async = true,
            IgnoreComments = true
        };
        if (encoding is null) return XmlReader.Create(input, settings);
        input.Position = preambleLength;
        var text = new StreamReader(input, encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        settings.CloseInput = true;
        return XmlReader.Create(text, settings);
    }

    private static (Encoding? Encoding, int PreambleLength) ReadByteOrderMark(ReadOnlySpan<byte> input) => input switch
    {
        [0xff, 0xfe, 0, 0, ..] => (new UTF32Encoding(false, false, true), 4),
        [0, 0, 0xfe, 0xff, ..] => (new UTF32Encoding(true, false, true), 4),
        [0xef, 0xbb, 0xbf, ..] => (new UTF8Encoding(false, true), 3),
        [0xff, 0xfe, ..] => (new UnicodeEncoding(false, false, true), 2),
        [0xfe, 0xff, ..] => (new UnicodeEncoding(true, false, true), 2),
        _ => (null, 0)
    };

    private static IEnumerable<string> ParseRss(XElement root)
    {
        foreach (var item in root.Elements("channel").Elements("item"))
            foreach (var link in item.Elements("link"))
            {
                if (link.HasElements) throw new InvalidDataException("RSS links cannot contain nested markup.");
                if (!string.IsNullOrWhiteSpace(link.Value)) yield return link.Value.Trim();
            }
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
