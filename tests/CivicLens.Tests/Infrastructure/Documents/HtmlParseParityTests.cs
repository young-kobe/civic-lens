using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CivicLens.Collection.Contracts;
using CivicLens.Collector.Http;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Documents;

public sealed class HtmlParseParityTests
{
    private const string Link = "<a href='/watch/story'>story</a>";

    public enum ParseOutcome { Accepted, LimitExceeded, Invalid, Unsupported }

    public static TheoryData<string, byte[], string, ParseOutcome> Inputs()
    {
        var cases = new TheoryData<string, byte[], string, ParseOutcome>();
        void Add(string name, byte[] bytes, string contentType, ParseOutcome expected) =>
            cases.Add(name, bytes, contentType, expected);
        void AddMarkup(string name, string markup, ParseOutcome expected, string contentType = "text/html; charset=utf-8") =>
            Add(name, Encoding.UTF8.GetBytes(markup), contentType, expected);

        AddMarkup("tokens just under the cap", Repeat("<br>", 99_000) + Link, ParseOutcome.Accepted);
        AddMarkup("tokens over the cap", Repeat("<br>", 100_001) + Link, ParseOutcome.LimitExceeded);
        AddMarkup("tokens over the cap by inline elements", Repeat("<b></b>", 50_001) + Link, ParseOutcome.LimitExceeded);
        foreach (var depth in new[] { 40, 60, 61, 62, 63, 64, 65, 66, 100 })
            AddMarkup($"{depth} nested divs", Repeat("<div>", depth) + Link, depth <= 60 ? ParseOutcome.Accepted : ParseOutcome.LimitExceeded);
        AddMarkup("200 unclosed paragraphs", Repeat("<p>text", 200) + Link, ParseOutcome.Accepted);
        AddMarkup("unclosed paragraphs with void elements", Repeat("<p>text<br><img src=x><hr>", 200) + Link, ParseOutcome.Accepted);
        AddMarkup("unclosed paragraphs inside a deep div chain", Repeat("<div>", 40) + Repeat("<p>text", 200) + Link, ParseOutcome.Accepted);
        AddMarkup("200 void elements in one paragraph", "<p>" + Repeat("<br><wbr><input>", 200) + Link, ParseOutcome.Accepted);
        AddMarkup("mismatched end tags", "<section><div><span>x</div></span></not-open>" + Repeat("<p>text", 100) + Link, ParseOutcome.Accepted);
        AddMarkup("paragraphs inside svg", "<svg><p>one<p>two</p></p></svg>" + Link, ParseOutcome.Accepted);
        AddMarkup("paragraphs inside math", "<math><p>one<p>two</p></p></math>" + Link, ParseOutcome.Accepted);
        AddMarkup("70 paragraphs inside svg", "<svg>" + Repeat("<p>x", 70) + "</svg>" + Link, ParseOutcome.LimitExceeded);
        AddMarkup("70 paragraphs inside math", "<math>" + Repeat("<p>x", 70) + "</math>" + Link, ParseOutcome.LimitExceeded);
        AddMarkup("self-closing svg and math", Repeat("<svg/><math/><svg><g/><path/></svg>", 100) + Link, ParseOutcome.Accepted);
        AddMarkup("self-closing elements inside svg", "<svg>" + Repeat("<g/>", 200) + "</svg>" + Link, ParseOutcome.Accepted);

        var cafe = "café " + Link;
        Add("utf-8 byte order mark over meta charset", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("<meta charset=windows-1252>" + cafe)],
            "text/html", ParseOutcome.Accepted);
        Add("utf-8 byte order mark over a header charset", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(cafe)],
            "text/html; charset=us-ascii", ParseOutcome.Accepted);
        Add("utf-16 byte order mark", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(cafe)],
            "text/html", ParseOutcome.Accepted);
        Add("big-endian utf-16 byte order mark", [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(cafe)],
            "text/html", ParseOutcome.Accepted);
        Add("utf-32 byte order mark", [0xff, 0xfe, 0x00, 0x00, .. Encoding.UTF32.GetBytes(cafe)], "text/html", ParseOutcome.Unsupported);
        Add("big-endian utf-32 byte order mark", [0x00, 0x00, 0xfe, 0xff, .. Encoding.UTF32.GetBytes(cafe)], "text/html", ParseOutcome.Unsupported);
        Add("meta charset without a byte order mark", Encoding.Latin1.GetBytes("<meta charset=iso-8859-1>" + cafe), "text/html", ParseOutcome.Accepted);
        Add("meta http-equiv charset", Encoding.Latin1.GetBytes("<meta http-equiv='Content-Type' content='text/html; charset=iso-8859-1'>" + cafe),
            "text/html", ParseOutcome.Accepted);
        Add("meta utf-8 with invalid bytes", [.. Encoding.UTF8.GetBytes("<meta charset=utf-8>"), 0xe9, .. Encoding.UTF8.GetBytes(Link)],
            "text/html", ParseOutcome.Invalid);
        Add("meta utf-16 is read as utf-8", Encoding.UTF8.GetBytes("<meta charset=utf-16>" + cafe), "text/html", ParseOutcome.Accepted);
        Add("meta charset after the first 1024 bytes", [.. Encoding.UTF8.GetBytes(Repeat(" ", 1100) + "<meta charset=nonsense>"), 0xe9, .. Encoding.UTF8.GetBytes(Link)],
            "text/html", ParseOutcome.Accepted);
        Add("header charset over meta charset", Encoding.Latin1.GetBytes("<meta charset=utf-8>" + cafe), "text/html; charset=iso-8859-1", ParseOutcome.Accepted);
        Add("unknown header charset", Encoding.UTF8.GetBytes(cafe), "text/html; charset=nonsense", ParseOutcome.Unsupported);
        Add("unknown meta charset", Encoding.UTF8.GetBytes("<meta charset=nonsense>" + cafe), "text/html", ParseOutcome.Unsupported);
        Add("x-user-defined header charset", Encoding.UTF8.GetBytes(cafe), "text/html; charset=x-user-defined", ParseOutcome.Unsupported);
        Add("iso-2022-kr header charset", Encoding.UTF8.GetBytes(cafe), "text/html; charset=iso-2022-kr", ParseOutcome.Unsupported);
        Add("replacement meta charset", Encoding.UTF8.GetBytes("<meta charset=replacement>" + cafe), "text/html", ParseOutcome.Unsupported);
        Add("windows-1252 bytes without a declaration", [0x93, 0x94, 0x80, 0x81, 0xe9, .. Encoding.UTF8.GetBytes(Link)], "text/html", ParseOutcome.Accepted);
        Add("windows-1252 bytes with a header charset", [0x93, 0x94, 0x80, 0xe9, .. Encoding.UTF8.GetBytes(Link)],
            "text/html; charset=windows-1252", ParseOutcome.Accepted);
        Add("invalid utf-8 without a declaration still reads as windows-1252", [0xc3, 0x28, .. Encoding.UTF8.GetBytes(Link)], "text/html", ParseOutcome.Accepted);
        return cases;
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public async Task CollectorAndDocumentExtractorTreatHostileAndEdgeHtmlTheSameWay(string name, byte[] bytes,
        string contentType, ParseOutcome expected)
    {
        var collector = await ParseWithCollectorAsync(bytes, contentType);
        var extractor = await ParseWithExtractorAsync(bytes, contentType);

        Assert.True(collector == extractor, $"{name}: collector gave {collector} but document extractor gave {extractor}.");
        Assert.True(expected == collector, $"{name}: expected {expected} but both gave {collector}.");
    }

    private static async Task<ParseOutcome> ParseWithCollectorAsync(byte[] bytes, string contentType)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-lens-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var handler = new RobotsThenPageHandler(bytes, contentType);
            using var collector = new HttpCollector(handler);
            var result = await collector.FetchAsync(CollectionFixtures.Request() with
            {
                Url = "https://example.test/watch",
                AllowedPathPrefix = "/watch",
                Mode = CollectionMode.Html,
                ArtifactDirectory = directory,
                MaxBytes = 5_000_000
            });
            Assert.Equal(CollectionOutcome.Captured, result.Outcome);
            return result.Discovery!.Status switch
            {
                DiscoveryStatus.Parsed => ParseOutcome.Accepted,
                DiscoveryStatus.LimitExceeded => ParseOutcome.LimitExceeded,
                DiscoveryStatus.Unsupported => ParseOutcome.Unsupported,
                _ => ParseOutcome.Invalid
            };
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<ParseOutcome> ParseWithExtractorAsync(byte[] bytes, string contentType)
    {
        await using var capture = await TestCapture.CreateAsync(bytes, contentType);
        try
        {
            await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, CancellationToken.None);
            return ParseOutcome.Accepted;
        }
        catch (NotSupportedException)
        {
            return ParseOutcome.Unsupported;
        }
        catch (InvalidDataException exception) when (exception.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return ParseOutcome.LimitExceeded;
        }
        catch (InvalidDataException)
        {
            return ParseOutcome.Invalid;
        }
    }

    private static string Repeat(string value, int count) => string.Concat(Enumerable.Repeat(value, count));

    private sealed class RobotsThenPageHandler(byte[] page, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/robots.txt")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(page) };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            return Task.FromResult(response);
        }
    }
}
