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
    private static readonly XName XmlBase = XNamespace.Xml + "base";

    public static async Task<DiscoveryResult> ParseCaptureAsync(string capturePath, CollectionRequest request,
        string responseUrl, IReadOnlyList<string> encodings, string? contentType, CancellationToken token)
    {
        var candidates = new DiscoveryCandidates(request);
        try
        {
            using var xml = await CaptureDecoder.ReadDecodedAsync(capturePath, encodings, MaximumDecodedCharacters, token);
            xml.Position = 0;
            using (var limitReader = CreateXmlReader(xml, contentType))
            {
                var nodes = 0;
                while (await limitReader.ReadAsync())
                {
                    token.ThrowIfCancellationRequested();
                    if (++nodes > MaximumNodes || limitReader.Depth > MaximumDepth)
                        return candidates.Failed(DiscoveryStatus.LimitExceeded);
                }
            }
            xml.Position = 0;
            using var reader = CreateXmlReader(xml, contentType);
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, token);
            var root = document.Root;
            if (root is null) return candidates.Failed(DiscoveryStatus.Invalid);
            if (request.Version < CollectionProtocol.SchemeUpgradeVersion &&
                root.DescendantsAndSelf().Attributes().Any(attribute => attribute.Name == XmlBase))
                return candidates.Failed(DiscoveryStatus.Unsupported);
            var links = root.Name.LocalName switch
            {
                "rss" when root.Name.NamespaceName.Length == 0 && (string?)root.Attribute("version") == "2.0" => ParseRss(root),
                "feed" when root.Name == XName.Get("feed", "http://www.w3.org/2005/Atom") => ParseAtom(root),
                _ => throw new UnsupportedFeedException()
            };
            var finalUrl = new Uri(responseUrl);
            foreach (var (element, link) in links)
            {
                token.ThrowIfCancellationRequested();
                if (link.Length > 4096 || !Uri.TryCreate(ResolveBase(element, finalUrl), link, out var resolved))
                    return candidates.Failed(DiscoveryStatus.Invalid);
                var rejection = candidates.Admit(resolved);
                if (rejection is not null) return candidates.Failed(rejection.Value);
            }
            return candidates.Parsed();
        }
        catch (UnsupportedEncodingException) { return candidates.Failed(DiscoveryStatus.Unsupported); }
        catch (UnsupportedFeedException) { return candidates.Failed(DiscoveryStatus.Unsupported); }
        catch (Exception exception) when (exception is XmlException or DecoderFallbackException or InvalidDataException or IOException or BoundedStreamLimitException)
        {
            return candidates.Failed(exception is BoundedStreamLimitException ? DiscoveryStatus.LimitExceeded : DiscoveryStatus.Invalid);
        }
    }

    private static Uri ResolveBase(XElement element, Uri responseUrl)
    {
        var resolved = responseUrl;
        foreach (var value in element.AncestorsAndSelf().Reverse().Select(item => (string?)item.Attribute(XmlBase)))
        {
            if (value is null) continue;
            if (value.Length > 4096 || !Uri.TryCreate(resolved, value.Trim(), out var next))
                throw new InvalidDataException("Feed xml:base is invalid.");
            resolved = next;
        }
        return resolved;
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

    private static IEnumerable<(XElement Element, string Href)> ParseRss(XElement root)
    {
        foreach (var item in root.Elements("channel").Elements("item"))
            foreach (var link in item.Elements("link"))
            {
                if (link.HasElements) throw new InvalidDataException("RSS links cannot contain nested markup.");
                if (!string.IsNullOrWhiteSpace(link.Value)) yield return (link, link.Value.Trim());
            }
    }

    private static IEnumerable<(XElement Element, string Href)> ParseAtom(XElement root)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        foreach (var entry in root.Elements(atom + "entry"))
            foreach (var link in entry.Elements(atom + "link"))
            {
                var rel = (string?)link.Attribute("rel");
                if (string.IsNullOrEmpty(rel) || rel == "alternate")
                {
                    var href = (string?)link.Attribute("href");
                    if (!string.IsNullOrWhiteSpace(href)) yield return (link, href.Trim());
                }
            }
    }

    private sealed class UnsupportedFeedException : Exception;
}
