using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Collection.Contracts;

namespace CivicLens.Collector.Http;

internal static class HtmlParser
{
    private const string HtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const int MaximumDecodedBytes = 10_000_000;
    private const int MaximumMarkupTokens = 100_000;
    private const int MaximumOpenElements = 64;
    private const int MaximumHrefLength = 4096;

    public static async Task<DiscoveryResult> ParseCaptureAsync(string capturePath, CollectionRequest request,
        string responseUrl, IReadOnlyList<string> encodings, string? contentType, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
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

                using var bounded = new BoundedReadStream(decoded, MaximumDecodedBytes);
                using var output = new MemoryStream();
                await bounded.CopyToAsync(output, 81920, token);
                token.ThrowIfCancellationRequested();
                var bytes = output.ToArray();
                var html = DecodeHtml(bytes, contentType);
                token.ThrowIfCancellationRequested();

                // AngleSharp does not fetch linked resources. This parser only builds a tree from
                // the already captured representation; scripts are never executed.
                using var document = await CreateParser(token).ParseDocumentAsync(html, token);
                token.ThrowIfCancellationRequested();
                ValidateTreeDepth(document, token);
                var finalUrl = new Uri(responseUrl);
                var resolutionBase = FindBase(document, finalUrl);
                var urls = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var anchor in document.QuerySelectorAll("a[href]"))
                {
                    token.ThrowIfCancellationRequested();
                    if (anchor.NamespaceUri != HtmlNamespace) continue;
                    var href = anchor.GetAttribute("href");
                    if (string.IsNullOrWhiteSpace(href)) continue;
                    if (href.Length > MaximumHrefLength) return Result(DiscoveryStatus.Invalid);
                    if (!Uri.TryCreate(resolutionBase, href.Trim(), out var resolved))
                        return Result(DiscoveryStatus.Invalid);
                    resolved = WithoutFragment(resolved);
                    if (!request.Allows(resolved)) continue;
                    var value = resolved.AbsoluteUri;
                    if (value.Length > MaximumHrefLength) return Result(DiscoveryStatus.Invalid);
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
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnsupportedEncodingException) { return Result(DiscoveryStatus.Unsupported); }
        catch (UnsupportedCharsetException) { return Result(DiscoveryStatus.Unsupported); }
        catch (Exception exception) when (exception is DecoderFallbackException or InvalidDataException or IOException or BoundedStreamLimitException or MarkupLimitException)
        {
            return Result(exception is BoundedStreamLimitException or MarkupLimitException ? DiscoveryStatus.LimitExceeded : DiscoveryStatus.Invalid);
        }

        static Stream Own(Stream stream, List<Stream> owned) { owned.Add(stream); return stream; }
        static DiscoveryResult Result(DiscoveryStatus status) => new() { Status = status, Urls = [] };
    }

    private static Uri FindBase(AngleSharp.Dom.IDocument document, Uri responseUrl)
    {
        foreach (var element in document.QuerySelectorAll("base[href]"))
        {
            if (element.NamespaceUri != HtmlNamespace) continue;
            var href = element.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || href.Length > MaximumHrefLength) continue;
            if (Uri.TryCreate(responseUrl, href.Trim(), out var candidate) &&
                candidate.Scheme is "http" or "https" && candidate.UserInfo.Length == 0)
                return candidate;
        }
        return responseUrl;
    }

    private static Uri WithoutFragment(Uri value)
    {
        if (value.Fragment.Length == 0) return value;
        var builder = new UriBuilder(value) { Fragment = string.Empty };
        return builder.Uri;
    }

    private static string DecodeHtml(byte[] bytes, string? contentType)
    {
        var (encoding, offset) = ReadBom(bytes);
        encoding ??= GetHttpEncoding(contentType);
        encoding ??= GetMetaEncoding(bytes);
        encoding ??= GetStrictEncoding("windows-1252");
        return encoding.GetString(bytes, offset, bytes.Length - offset);
    }

    private static Encoding? GetHttpEncoding(string? contentType)
    {
        if (contentType is null) return null;
        var charset = MediaTypeHeaderValue.Parse(contentType).CharSet?.Trim('"').Trim(' ', '\t', '\r', '\n', '\f');
        // The library maps this label to Windows-1252 for meta declarations,
        // but an HTTP declaration requires a distinct decoder we do not support.
        if (string.Equals(charset, "x-user-defined", StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedCharsetException();
        return charset is null ? null : GetStrictEncoding(charset);
    }

    private static Encoding? GetMetaEncoding(byte[] bytes)
    {
        var count = Math.Min(bytes.Length, 1024);
        var prefix = Encoding.ASCII.GetString(bytes, 0, count);
        using var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(prefix);
        foreach (var meta in document.QuerySelectorAll("meta"))
        {
            if (meta.NamespaceUri != HtmlNamespace) continue;
            var charset = meta.GetAttribute("charset");
            if (charset is null && string.Equals(meta.GetAttribute("http-equiv"), "content-type", StringComparison.OrdinalIgnoreCase) &&
                MediaTypeHeaderValue.TryParse(meta.GetAttribute("content"), out var content))
                charset = content.CharSet?.Trim('"');
            if (string.IsNullOrWhiteSpace(charset)) continue;
            var encoding = GetStrictEncoding(charset);
            // HTML meta declarations cannot select UTF-16 without a byte order mark.
            return encoding.CodePage is 1200 or 1201 ? GetStrictEncoding("utf-8") : encoding;
        }
        return null;
    }

    private static Encoding GetStrictEncoding(string name)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            name = name.Trim(' ', '\t', '\r', '\n', '\f');
            if (name.ToLowerInvariant() is "replacement" or "iso-2022-cn" or "iso-2022-cn-ext" or
                "iso-2022-kr" or "csiso2022kr" or "hz-gb-2312")
                throw new UnsupportedCharsetException();
            if (!AngleSharp.Text.TextEncoding.IsSupported(name)) throw new UnsupportedCharsetException();
            var encoding = AngleSharp.Text.TextEncoding.Resolve(name);
            return Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new UnsupportedCharsetException();
        }
    }

    private static (Encoding? Encoding, int Offset) ReadBom(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x00, 0x00, 0xfe, 0xff, ..] => throw new UnsupportedCharsetException(),
        [0xff, 0xfe, 0x00, 0x00, ..] => throw new UnsupportedCharsetException(),
        [0xef, 0xbb, 0xbf, ..] => (new UTF8Encoding(false, true), 3),
        [0xfe, 0xff, ..] => (new UnicodeEncoding(true, false, true), 2),
        [0xff, 0xfe, ..] => (new UnicodeEncoding(false, false, true), 2),
        _ => (null, 0)
    };

    private static AngleSharp.Html.Parser.HtmlParser CreateParser(CancellationToken cancellationToken)
    {
        var tokens = 0;
        var elements = 0;
        var openElements = new List<string>();
        var createdElements = new List<IElement>();
        return new AngleSharp.Html.Parser.HtmlParser(new HtmlParserOptions
        {
            OnCreated = (element, _) =>
            {
                if (++elements > MaximumMarkupTokens) throw new MarkupLimitException();
                createdElements.Add(element);
            },
            OnToken = (htmlToken, _) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++tokens > MaximumMarkupTokens) throw new MarkupLimitException();
                foreach (var element in createdElements) ValidateAncestorDepth(element);
                createdElements.Clear();
                if (htmlToken.Type == HtmlTokenType.EndTag)
                {
                    if (openElements.Count > 0 && openElements[^1] == htmlToken.Name)
                        openElements.RemoveAt(openElements.Count - 1);
                }
                else if (htmlToken.Type == HtmlTokenType.StartTag && !IsVoidElement(htmlToken.Name))
                {
                    // A conservative lexical bound also covers malformed markup whose
                    // optional end tags a browser would implicitly close.
                    openElements.Add(htmlToken.Name);
                    if (openElements.Count > MaximumOpenElements) throw new MarkupLimitException();
                }
            }
        });
    }

    private static void ValidateAncestorDepth(IElement element)
    {
        var depth = 0;
        for (INode? current = element; current is not null; current = current.Parent)
            if (++depth > MaximumOpenElements) throw new MarkupLimitException();
    }

    private static void ValidateTreeDepth(IDocument document, CancellationToken cancellationToken)
    {
        var pending = new Stack<(INode Node, int Depth)>();
        pending.Push((document, 0));
        while (pending.TryPop(out var entry))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Depth > MaximumOpenElements) throw new MarkupLimitException();
            foreach (var child in entry.Node.ChildNodes) pending.Push((child, entry.Depth + 1));
        }
    }

    private static bool IsVoidElement(string name) => name is "area" or "base" or "br" or "col" or "embed" or
        "hr" or "img" or "input" or "link" or "meta" or "param" or "source" or "track" or "wbr";

    private sealed class MarkupLimitException : Exception;

    private sealed class UnsupportedEncodingException : Exception;
    private sealed class UnsupportedCharsetException : Exception;
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
