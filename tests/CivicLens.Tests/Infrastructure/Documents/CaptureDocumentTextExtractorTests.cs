using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Documents;

namespace CivicLens.Tests.Infrastructure.Documents;

public sealed class CaptureDocumentTextExtractorTests
{
    [Fact]
    public async Task ExtractsPlainTextAndUsesBomBeforeHttpCharset()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.Unicode.GetPreamble().Concat(
            Encoding.Unicode.GetBytes("café  title\nsecond line")).ToArray(), "text/plain; charset=us-ascii");

        var text = await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, CancellationToken.None);

        Assert.Equal("café  title\nsecond line", text);
        Assert.Equal("capture-text-v1", capture.Extractor.ParserVersion);
        Assert.Equal("body-text-v1", capture.Extractor.NormalizationVersion);
    }

    [Fact]
    public async Task ExtractsHtmlTextWithBlockSeparatorsAndSkipsNonVisibleElements()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(
            "<html><head><title>head</title><style>hidden-style</style></head><body><h1>One&nbsp; two</h1>" +
            "<p>first <b>part</b></p><script>hidden-script</script><template>hidden-template</template>" +
            "<noscript>hidden-noscript</noscript><div>last</div></body></html>"), "text/html; charset=utf-8");

        var text = await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, CancellationToken.None);

        Assert.Equal("One two\nfirst part\nlast", text);
    }

    [Fact]
    public async Task AppliesHttpCharsetWhenHtmlHasNoBomOrMeta()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1252).GetBytes("<p>café</p>");
        await using var capture = await TestCapture.CreateAsync(bytes, "text/html; charset=windows-1252");

        Assert.Equal("café", await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsModifiedCaptureBytesBeforeDecoding()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes("original"), "text/plain");
        await File.WriteAllBytesAsync(capture.Path, Compress(Encoding.UTF8.GetBytes("modified")));

        await Assert.ThrowsAsync<InvalidDataException>(() => capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, CancellationToken.None));
    }

    [Fact]
    public async Task SupportsStackedHttpContentCodingsInReverseOrder()
    {
        var body = Encoding.UTF8.GetBytes("<p>stacked ✓</p>");
        var encoded = Compress(body);
        await using var capture = await TestCapture.CreateAsync(Compress(encoded), "text/html; charset=utf-8", ["gzip", "gzip"]);

        Assert.Equal("stacked ✓", await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsUnsupportedTypeCharsetAndMalformedText()
    {
        await using var unsupported = await TestCapture.CreateAsync([1, 2], "application/pdf");
        await Assert.ThrowsAsync<NotSupportedException>(() => unsupported.Extractor.ExtractAsync(unsupported.Attempt, unsupported.Root, CancellationToken.None));

        await using var charset = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes("x"), "text/plain; charset=not-a-real-charset");
        await Assert.ThrowsAsync<NotSupportedException>(() => charset.Extractor.ExtractAsync(charset.Attempt, charset.Root, CancellationToken.None));

        await using var malformed = await TestCapture.CreateAsync([0xc3, 0x28], "text/plain; charset=utf-8");
        await Assert.ThrowsAsync<InvalidDataException>(() => malformed.Extractor.ExtractAsync(malformed.Attempt, malformed.Root, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsDecodedInputAndOutputOverLimits()
    {
        await using var input = await TestCapture.CreateAsync(new byte[10_000_001], "text/plain");
        await Assert.ThrowsAsync<InvalidDataException>(() => input.Extractor.ExtractAsync(input.Attempt, input.Root, CancellationToken.None));

        await using var output = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes("x".PadRight(2_000_001, 'x')), "text/plain");
        await Assert.ThrowsAsync<InvalidDataException>(() => output.Extractor.ExtractAsync(output.Attempt, output.Root, CancellationToken.None));
    }

    [Fact]
    public async Task HonorsCancellationBeforeReadingCapture()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes("text"), "text/plain");
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, source.Token));
    }

    [Fact]
    public async Task ProfileSelectsOneBodyRootAndIgnoresNavigationChanges()
    {
        var profile = new DocumentContentProfile("story", "#article", []);
        await using var first = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(
            "<nav>Old navigation</nav><main id=article><h1>Story title</h1><p>Evidence text</p></main>"), "text/html");
        await using var second = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(
            "<nav>New navigation</nav><main id=article><h1>Story title</h1><p>Evidence text</p></main>"), "text/html");

        var firstText = await first.Extractor.ExtractAsync(first.Attempt, first.Root, profile, CancellationToken.None);
        var secondText = await second.Extractor.ExtractAsync(second.Attempt, second.Root, profile, CancellationToken.None);

        Assert.Equal("Story title\nEvidence text", firstText);
        Assert.Equal(firstText, secondText);
    }

    [Fact]
    public async Task NullProfilePreservesBodyExtractionAndVersionLabels()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(
            "<head><title>not body</title></head><body><nav>Navigation</nav><main>Story</main></body>"), "text/html");

        var text = await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, null, CancellationToken.None);

        Assert.Equal("Navigation\nStory", text);
        Assert.Equal("capture-text-v1", capture.Extractor.ParserVersion);
        Assert.Equal("body-text-v1", capture.Extractor.NormalizationVersion);
    }

    [Fact]
    public async Task ProfileSupportsSharedTagAndClassSelectorsAndExclusions()
    {
        var profile = new DocumentContentProfile("story", "article", [".metadata", "#share"]);
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(
            "<body><article><p>First <span class=metadata>author</span> paragraph</p>" +
            "<div class=metadata>hidden block</div><p>Second <b id=share>shared</b> paragraph</p></article></body>"), "text/html");

        var text = await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, profile, CancellationToken.None);

        Assert.Equal("First paragraph\nSecond paragraph", text);
    }

    [Fact]
    public async Task ProfileRequiresExactlyOneMatchAndDoesNotFallback()
    {
        await using var missing = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes("<p>fallback text</p>"), "text/html");
        await Assert.ThrowsAsync<InvalidDataException>(() => missing.Extractor.ExtractAsync(missing.Attempt, missing.Root,
            new DocumentContentProfile("story", "article", []), CancellationToken.None));

        await using var ambiguous = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes("<article>a</article><article>b</article>"), "text/html");
        await Assert.ThrowsAsync<InvalidDataException>(() => ambiguous.Extractor.ExtractAsync(ambiguous.Attempt, ambiguous.Root,
            new DocumentContentProfile("story", "article", []), CancellationToken.None));
    }

    [Fact]
    public async Task ProfileRejectsExcludedRootsAndForbiddenContentRoots()
    {
        await using var excluded = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes("<main id=story>text</main>"), "text/html");
        await Assert.ThrowsAsync<InvalidDataException>(() => excluded.Extractor.ExtractAsync(excluded.Attempt, excluded.Root,
            new DocumentContentProfile("story", "#story", ["#story"]), CancellationToken.None));

        await using var forbidden = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes("<script id=story>bad</script>"), "text/html");
        await Assert.ThrowsAsync<InvalidDataException>(() => forbidden.Extractor.ExtractAsync(forbidden.Attempt, forbidden.Root,
            new DocumentContentProfile("story", "#story", []), CancellationToken.None));
    }

    [Fact]
    public async Task ProfilePreservesBoundariesAroundRemovedInlineAndBlockElements()
    {
        var profile = new DocumentContentProfile("story", "main", [".removed"]);
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(
            "<main>A<span class=removed>inline</span>B<div class=removed>block</div>C</main>"), "text/html");

        Assert.Equal("A B\nC", await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, profile, CancellationToken.None));
    }

    [Fact]
    public async Task ProfileAppliesOnlyToHtml()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes("plain text"), "text/plain");

        await Assert.ThrowsAsync<NotSupportedException>(() => capture.Extractor.ExtractAsync(capture.Attempt,
            capture.Root, new DocumentContentProfile("story", "main", []), CancellationToken.None));
    }

    [Fact]
    public async Task ClassSelectorsUseHtmlWhitespaceAndExactClassNames()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(
            "<main class='policy&#160;other'>wrong</main><article class='x\tpolicy\ny'>selected</article>"), "text/html; charset=utf-8");
        var profile = new DocumentContentProfile("policy", ".policy", []);
        Assert.Equal("selected", await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, profile, CancellationToken.None));
    }

    [Theory]
    [InlineData("<p>first</p>tail", "first\ntail")]
    [InlineData("<div>first</div><span>tail</span>", "first\ntail")]
    [InlineData("<p>first<br>second</p>", "first\nsecond")]
    [InlineData("<p>A<b>B</b>C</p>", "ABC")]
    public async Task PreservesBlockEndAndInlineTextBoundaries(string html, string expected)
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(html), "text/html; charset=\"utf-8\"");
        Assert.Equal(expected, await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, CancellationToken.None));
    }

    [Theory]
    [InlineData("<meta charset=utf-8><p>café</p>", "café")]
    [InlineData("<script>var s = '<meta charset=wrong>'</script><meta charset=utf-8><p>café</p>", "café")]
    public async Task UsesActualMetaDeclarations(string html, string expected)
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(html), "text/html");
        Assert.Equal(expected, await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsExcessiveMarkupDepthAndTruncatedHttpEncoding()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 65)) + "text";
        await using var nested = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(html), "text/html");
        await Assert.ThrowsAsync<InvalidDataException>(() => nested.Extractor.ExtractAsync(nested.Attempt, nested.Root, CancellationToken.None));
        var incomplete = Compress(Encoding.UTF8.GetBytes("text"))[..^4];
        await using var truncated = await TestCapture.CreateAsync(incomplete, "text/plain", ["gzip"]);
        await Assert.ThrowsAsync<InvalidDataException>(() => truncated.Extractor.ExtractAsync(truncated.Attempt, truncated.Root, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsHttpExpansionBeyondInputLimit()
    {
        await using var capture = await TestCapture.CreateAsync(Compress(new byte[10_000_001]), "text/plain", ["gzip"]);
        await Assert.ThrowsAsync<InvalidDataException>(() => capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, CancellationToken.None));
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task ReadsSupportedHttpEncodingsAndRejectsTruncatedOuterLayer(string coding)
    {
        var bytes = Encoding.UTF8.GetBytes("source text");
        using var output = new MemoryStream();
        using (Stream encoder = coding switch
        {
            "gzip" => new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true),
            "deflate" => new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true),
            _ => new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true)
        }) encoder.Write(bytes);
        var encoded = output.ToArray();
        await using var valid = await TestCapture.CreateAsync(encoded, "text/plain", [coding]);
        Assert.Equal("source text", await valid.Extractor.ExtractAsync(valid.Attempt, valid.Root, CancellationToken.None));
        await using var incomplete = await TestCapture.CreateAsync(Compress(encoded)[..^4], "text/plain", [coding, "gzip"]);
        await Assert.ThrowsAsync<InvalidDataException>(() => incomplete.Extractor.ExtractAsync(incomplete.Attempt, incomplete.Root, CancellationToken.None));
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(bytes);
        return output.ToArray();
    }

    private sealed class TestCapture : IAsyncDisposable
    {
        private TestCapture(string root, byte[] entity, string contentType, IReadOnlyList<string> encodings)
        {
            Root = root;
            Path = System.IO.Path.Combine(root, Convert.ToHexStringLower(SHA256.HashData(entity)) + ".gz");
            Attempt = new CapturedAttemptResult("attempt", "source", "https://example.test/a", "https://example.test/a",
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                new CollectionResponse(200, null, null, contentType, encodings),
                new CaptureIdentity(Convert.ToHexStringLower(SHA256.HashData(entity)), entity.Length));
        }

        public string Root { get; }
        public string Path { get; }
        public CapturedAttemptResult Attempt { get; }
        public CaptureDocumentTextExtractor Extractor { get; } = new();

        public static async Task<TestCapture> CreateAsync(byte[] entity, string contentType,
            IReadOnlyList<string>? encodings = null)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "civic-lens-extraction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var capture = new TestCapture(root, entity, contentType, encodings ?? []);
            await using var file = File.Create(capture.Path);
            await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
            await gzip.WriteAsync(entity);
            return capture;
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(Root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
