using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens;
using CivicLens.Application.Documents;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Infrastructure.Documents;

public sealed class CaptureDocumentTextExtractor : IDocumentTextExtractor
{
    private const string HtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const int MaximumInputBytes = 10_000_000;
    private const int MaximumMarkupTokens = 100_000;
    private const int MaximumTreeDepth = 64;
    private const int MaximumTextLength = 2_000_000;
    private static readonly HashSet<string> BlockElements = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "dd", "div", "dl", "dt", "fieldset", "figcaption",
        "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main",
        "nav", "ol", "p", "pre", "section", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "ul"
    };
    private static readonly HashSet<string> IgnoredElements = new(StringComparer.Ordinal)
        { "script", "style", "template", "noscript" };

    public string ParserVersion => "capture-text-v2";
    public string NormalizationVersion => "body-text-v1";

    public async Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot,
        CancellationToken cancellationToken) => await ExtractAsync(attempt, artifactRoot, null, cancellationToken);

    public async Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot,
        DocumentContentProfile? profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var contentType = attempt.Response.ContentType;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            throw new InvalidDataException("Capture has no valid content type.");

        var path = Path.Combine(artifactRoot, attempt.Capture.Sha256 + ".gz");
        var entity = await ReadAndVerifyCaptureAsync(path, attempt.Capture, cancellationToken);
        var decoded = await DecodeContentAsync(entity, attempt.Response.ContentEncodings, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string text;
        try
        {
            text = mediaType.MediaType?.ToLowerInvariant() switch
            {
                "text/plain" when profile is null => DecodePlainText(decoded, mediaType.CharSet?.Trim('"')),
                "text/html" => await DecodeHtmlTextAsync(decoded, mediaType.CharSet?.Trim('"'), profile, cancellationToken),
                _ => throw new NotSupportedException("Content type is not supported.")
            };
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Captured text is malformed for its character encoding.", exception);
        }
        ValidateText(text);
        return text;
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

    private static string DecodePlainText(byte[] bytes, string? charset)
    {
        var (encoding, offset) = ReadBom(bytes);
        if (encoding is null)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try
            {
                encoding = charset is null ? new UTF8Encoding(false, true) :
                    Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            catch (ArgumentException exception) { throw new NotSupportedException("Charset is not supported.", exception); }
        }
        return encoding.GetString(bytes, offset, bytes.Length - offset);
    }

    private static async Task<string> DecodeHtmlTextAsync(byte[] bytes, string? charset,
        DocumentContentProfile? profile, CancellationToken cancellationToken)
    {
        var (encoding, offset) = ReadBom(bytes);
        encoding ??= charset is null ? null : GetStrictEncoding(charset);
        encoding ??= GetMetaEncoding(bytes);
        encoding ??= GetStrictEncoding("windows-1252");
        var markup = encoding.GetString(bytes, offset, bytes.Length - offset);
        cancellationToken.ThrowIfCancellationRequested();
        using var document = await CreateParser(cancellationToken).ParseDocumentAsync(markup, cancellationToken);
        ValidateTreeDepth(document, cancellationToken);
        return profile is null
            ? ExtractBodyText(document, cancellationToken)
            : ExtractProfileText(document, profile, cancellationToken);
    }

    private static string ExtractProfileText(IDocument document, DocumentContentProfile profile,
        CancellationToken cancellationToken)
    {
        var body = document.Body ?? throw new InvalidDataException("HTML document has no body for the content profile.");
        var matches = FindMatches(body, profile.Selector, cancellationToken);
        if (matches.Count == 0) throw new InvalidDataException("Content profile selector did not match an HTML body element.");
        if (matches.Count > 1) throw new InvalidDataException("Content profile selector matched more than one HTML body element.");

        var root = matches[0];
        if (IsIgnoredRoot(root)) throw new InvalidDataException("Content profile selected an excluded element type.");
        var exclusions = new HashSet<IElement>();
        foreach (var selector in profile.ExcludedSelectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MatchesSelector(root, selector, cancellationToken))
                throw new InvalidDataException("Content profile exclusion matches the selected root.");
            foreach (var match in FindMatches(root, selector, cancellationToken)) exclusions.Add(match);
        }

        return ExtractTextFromRoot(root, exclusions, cancellationToken);
    }

    private static List<IElement> FindMatches(IElement root, string selector, CancellationToken cancellationToken)
    {
        var matches = new List<IElement>();
        var pending = new Stack<INode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is not IElement element) continue;
            if (element.NamespaceUri == HtmlNamespace && MatchesSelector(element, selector, cancellationToken))
                matches.Add(element);
            if (IgnoredElements.Contains(element.LocalName.ToLowerInvariant())) continue;
            var children = element.ChildNodes;
            for (var index = children.Length - 1; index >= 0; index--) pending.Push(children[index]);
        }
        return matches;
    }

    private static bool MatchesSelector(IElement element, string selector, CancellationToken cancellationToken)
    {
        if (selector[0] == '#') return element.Id.AsSpan().SequenceEqual(selector.AsSpan(1));
        if (selector[0] == '.')
        {
            var classes = element.GetAttribute("class").AsSpan();
            while (!classes.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var end = classes.IndexOfAny(" \t\r\n\f");
                if (end < 0) return classes.SequenceEqual(selector.AsSpan(1));
                if (classes[..end].SequenceEqual(selector.AsSpan(1))) return true;
                classes = classes[(end + 1)..];
            }
            return false;
        }
        return string.Equals(element.LocalName, selector, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredRoot(IElement element) => IgnoredElements.Contains(element.LocalName.ToLowerInvariant());

    private static string ExtractBodyText(IDocument document, CancellationToken cancellationToken)
    {
        var body = document.Body;
        return body is null ? string.Empty : ExtractTextFromRoot(body, new HashSet<IElement>(), cancellationToken);
    }

    private static string ExtractTextFromRoot(IElement root, HashSet<IElement> exclusions,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var pendingSpace = false;
        var lineBreakPending = false;
        var stack = new Stack<(INode Node, bool Closing)>();
        stack.Push((root, false));
        while (stack.TryPop(out var entry))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = entry.Node;
            if (node is IElement element)
            {
                var name = element.LocalName.ToLowerInvariant();
                if (IgnoredElements.Contains(name)) continue;
                if (exclusions.Contains(element))
                {
                    if (BlockElements.Contains(name)) lineBreakPending = true;
                    else pendingSpace = true;
                    continue;
                }
                if (BlockElements.Contains(name)) lineBreakPending = true;
                if (entry.Closing) continue;
                if (name == "br") lineBreakPending = true;
                stack.Push((element, true));
                var children = element.ChildNodes;
                for (var index = children.Length - 1; index >= 0; index--) stack.Push((children[index], false));
            }
            else if (node is IText text)
            {
                AppendNormalized(text.Data, output, ref pendingSpace, ref lineBreakPending, cancellationToken);
            }
        }
        TrimTrailingWhitespace(output);
        return output.ToString();
    }

    private static void AppendNormalized(string value, StringBuilder output, ref bool pendingSpace,
        ref bool lineBreakPending, CancellationToken cancellationToken)
    {
        foreach (var character in value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (char.IsWhiteSpace(character)) { pendingSpace = true; continue; }
            if (lineBreakPending)
            {
                TrimTrailingWhitespace(output);
                if (output.Length > 0) AppendBounded(output, '\n');
                lineBreakPending = false;
            }
            if (pendingSpace && output.Length > 0 && output[^1] != '\n') AppendBounded(output, ' ');
            pendingSpace = false;
            AppendBounded(output, character);
        }
    }

    private static void AppendBounded(StringBuilder output, char value)
    {
        if (output.Length >= MaximumTextLength) throw new InvalidDataException("Extracted text exceeds the output limit.");
        output.Append(value);
    }

    private static void TrimTrailingWhitespace(StringBuilder output)
    {
        while (output.Length > 0 && output[^1] is ' ' or '\n') output.Length--;
    }

    private static void ValidateText(string text)
    {
        if (text.Length > MaximumTextLength) throw new InvalidDataException("Extracted text exceeds the output limit.");
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\0') throw new InvalidDataException("Extracted text contains NUL.");
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    throw new InvalidDataException("Extracted text contains an invalid surrogate.");
                index++;
            }
            else if (char.IsLowSurrogate(character)) throw new InvalidDataException("Extracted text contains an invalid surrogate.");
        }
    }

    private static HtmlParser CreateParser(CancellationToken cancellationToken)
    {
        var tokens = 0;
        var elements = 0;
        var openElements = new List<string>();
        var createdElements = new List<IElement>();
        return new HtmlParser(new HtmlParserOptions
        {
            OnCreated = (element, _) =>
            {
                if (++elements > MaximumMarkupTokens) throw new InvalidDataException("HTML element limit exceeded.");
                createdElements.Add(element);
            },
            OnToken = (htmlToken, _) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++tokens > MaximumMarkupTokens) throw new InvalidDataException("HTML token limit exceeded.");
                foreach (var element in createdElements)
                {
                    var depth = 0;
                    for (INode? current = element; current is not null; current = current.Parent)
                        if (++depth > MaximumTreeDepth) throw new InvalidDataException("HTML nesting limit exceeded.");
                }
                createdElements.Clear();
                if (htmlToken.Type == HtmlTokenType.EndTag)
                {
                    CloseThroughMatchingAncestor(openElements, htmlToken.Name);
                }
                else if (htmlToken.Type == HtmlTokenType.StartTag && !IsVoidElement(htmlToken.Name))
                {
                    if (htmlToken is HtmlTagToken { IsSelfClosing: true } &&
                        (htmlToken.Name is "svg" or "math" || openElements.Contains("svg") || openElements.Contains("math"))) return;
                    if (ClosesParagraph(htmlToken.Name)) CloseNearestParagraph(openElements);
                    openElements.Add(htmlToken.Name);
                    if (openElements.Count > MaximumTreeDepth) throw new InvalidDataException("HTML nesting limit exceeded.");
                }
            }
        });
    }

    private static void CloseThroughMatchingAncestor(List<string> openElements, string name)
    {
        var matchingIndex = openElements.LastIndexOf(name);
        if (matchingIndex >= 0) openElements.RemoveRange(matchingIndex, openElements.Count - matchingIndex);
    }

    private static void CloseNearestParagraph(List<string> openElements)
    {
        // Do not apply HTML paragraph rules inside SVG or MathML foreign content.
        if (openElements.Contains("svg", StringComparer.Ordinal) || openElements.Contains("math", StringComparer.Ordinal)) return;
        var paragraphIndex = openElements.LastIndexOf("p");
        if (paragraphIndex >= 0) openElements.RemoveRange(paragraphIndex, openElements.Count - paragraphIndex);
    }

    private static bool ClosesParagraph(string name) => name is "address" or "article" or "aside" or "blockquote" or
        "details" or "div" or "dl" or "fieldset" or "figcaption" or "figure" or "footer" or "form" or
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "header" or "hr" or "main" or "menu" or
        "nav" or "ol" or "p" or "pre" or "section" or "table" or "ul";

    private static bool IsVoidElement(string name) => name is "area" or "base" or "br" or "col" or "embed" or
        "hr" or "img" or "input" or "link" or "meta" or "param" or "source" or "track" or "wbr";

    private static void ValidateTreeDepth(IDocument document, CancellationToken cancellationToken)
    {
        var pending = new Stack<(INode Node, int Depth)>();
        pending.Push((document, 0));
        while (pending.TryPop(out var entry))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Depth > MaximumTreeDepth) throw new InvalidDataException("HTML nesting limit exceeded.");
            foreach (var child in entry.Node.ChildNodes) pending.Push((child, entry.Depth + 1));
        }
    }

    private static Encoding? GetMetaEncoding(byte[] bytes)
    {
        var count = Math.Min(bytes.Length, 1024);
        var prefix = Encoding.ASCII.GetString(bytes, 0, count);
        using var document = new HtmlParser().ParseDocument(prefix);
        foreach (var meta in document.QuerySelectorAll("meta"))
        {
            if (meta.NamespaceUri != HtmlNamespace) continue;
            var charset = meta.GetAttribute("charset");
            if (charset is null && string.Equals(meta.GetAttribute("http-equiv"), "content-type", StringComparison.OrdinalIgnoreCase) &&
                MediaTypeHeaderValue.TryParse(meta.GetAttribute("content"), out var content)) charset = content.CharSet?.Trim('"');
            if (string.IsNullOrWhiteSpace(charset)) continue;
            if (charset.Trim().Equals("x-user-defined", StringComparison.OrdinalIgnoreCase)) charset = "windows-1252";
            var encoding = GetStrictEncoding(charset);
            return encoding.CodePage is 1200 or 1201 ? GetStrictEncoding("utf-8") : encoding;
        }
        return null;
    }

    private static Encoding GetStrictEncoding(string name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        name = name.Trim(' ', '\t', '\r', '\n', '\f');
        if (name.ToLowerInvariant() is "x-user-defined" or "replacement" or "iso-2022-cn" or "iso-2022-cn-ext" or
            "iso-2022-kr" or "csiso2022kr" or "hz-gb-2312")
            throw new NotSupportedException("Charset is not supported.");
        if (!AngleSharp.Text.TextEncoding.IsSupported(name)) throw new NotSupportedException($"Charset '{name}' is not supported.");
        var resolved = AngleSharp.Text.TextEncoding.Resolve(name);
        return Encoding.GetEncoding(resolved.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static (Encoding? Encoding, int Offset) ReadBom(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x00, 0x00, 0xfe, 0xff, ..] or [0xff, 0xfe, 0x00, 0x00, ..] => throw new NotSupportedException("UTF-32 is not supported."),
        [0xef, 0xbb, 0xbf, ..] => (new UTF8Encoding(false, true), 3),
        [0xfe, 0xff, ..] => (new UnicodeEncoding(true, false, true), 2),
        [0xff, 0xfe, ..] => (new UnicodeEncoding(false, false, true), 2),
        _ => (null, 0)
    };

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
