using System.Collections.Immutable;
using CivicLens.Core.Collection;

namespace CivicLens.Tests.Core.Collection;

public sealed class CollectionImportPolicyTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private readonly CollectionImportPolicy policy = new();

    [Fact]
    public void EqualSeparateInstanceIsDuplicateAndPreservesResolved304Link()
    {
        var prior = Captured("prior");
        var incoming = NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl);
        var validators = new SentValidators("\"v1\"", null);
        var persisted = policy.Decide(incoming, priorCapturedAttempt: prior, sentValidators: validators);

        var equalInput = NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl);
        Assert.Equal(incoming, equalInput);
        var duplicate = policy.Decide(equalInput, persisted, sentValidators: validators);

        Assert.Equal(ImportDisposition.DuplicateAttempt, duplicate.Disposition);
        Assert.Equal(PriorCaptureLinkStatus.Linked, duplicate.PriorCaptureLinkStatus);
        Assert.Same(prior, duplicate.PriorCapturedAttempt);
        Assert.Throws<InvalidOperationException>(() => policy.Decide(incoming, persisted,
            sentValidators: new SentValidators("\"different\"", null)));
    }

    [Fact]
    public void DuplicateAttemptConflictsOnResponseMetadataIncludingEncodingOrder()
    {
        var captured = Captured("attempt");
        var persisted = policy.Decide(captured);
        var reordered = new CapturedAttemptResult("attempt", captured.SourceId, captured.RequestedUrl, captured.FinalUrl,
            captured.ObservedAt, new CollectionResponse(200, "\"v1\"", ObservedAt, "text/html", ["gzip", "br"]),
            captured.Capture);
        Assert.Throws<InvalidOperationException>(() => policy.Decide(reordered, persisted));

        var otherOutcome = new FailedAttemptResult("attempt", captured.SourceId, captured.RequestedUrl, captured.FinalUrl,
            captured.ObservedAt, "network");
        Assert.False(captured.Equals(otherOutcome));
        Assert.Throws<InvalidOperationException>(() => policy.Decide(otherOutcome, persisted));
        Assert.Throws<InvalidOperationException>(() => policy.Decide(captured, policy.Decide(Captured("other"))));
    }

    [Fact]
    public void IdenticalCaptureBytesOnDifferentAttemptsRemainSeparateResults()
    {
        var first = policy.Decide(Captured("attempt-1"));
        var second = policy.Decide(Captured("attempt-2"));
        Assert.NotSame(first.AttemptResult, second.AttemptResult);
        Assert.Equal(first.Capture, second.Capture);
        Assert.Equal(ImportDisposition.NewAttempt, second.Disposition);
    }

    [Fact]
    public void ResponseEncodingsAreDefensivelySnapshottedAndComparedByValue()
    {
        var encodings = new[] { "br", "gzip" };
        var response = new CollectionResponse(200, null, null, null, encodings);
        encodings[0] = "changed";
        Assert.Equal(new[] { "br", "gzip" }, response.ContentEncodings);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)response.ContentEncodings)[0] = "changed");
        Assert.Equal(response, new CollectionResponse(200, null, null, null, ["br", "gzip"]));
        Assert.Empty(new CollectionResponse(200, null, null, null, Array.Empty<string>()).ContentEncodings);
        Assert.Throws<ArgumentException>(() => new CollectionResponse(200, null, null, null,
            default(ImmutableArray<string>)));
    }

    [Fact]
    public void NotModifiedLinksOnlyWhenSourceUrlsAndSentValidatorBindPriorCapture()
    {
        var prior = Captured("prior");
        var unchanged = NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl);
        var decision = policy.Decide(unchanged, priorCapturedAttempt: prior,
            sentValidators: new SentValidators("\"v1\"", null));
        Assert.Equal(PriorCaptureLinkStatus.Linked, decision.PriorCaptureLinkStatus);
        Assert.Same(prior, decision.PriorCapturedAttempt);
        Assert.Equal(304, decision.AttemptResult.Response!.StatusCode);
        Assert.Equal("\"v1\"", decision.AttemptResult.Response.ETag);
        Assert.Null(decision.AttemptResult.Response.ContentType);

        AssertUnresolved(unchanged, Captured("prior", source: "other"), new SentValidators("\"v1\"", null));
        AssertUnresolved(unchanged, Captured("prior", final: "https://example.test/redirect"), new SentValidators("\"v1\"", null));
        AssertUnresolved(unchanged, prior, new SentValidators("\"v2\"", null));
        AssertUnresolved(unchanged, prior, new SentValidators("\"v2\"", ObservedAt));
        AssertUnresolved(NotModified("attempt", prior.SourceId, prior.RequestedUrl, "https://example.test/redirect"),
            Captured("prior", final: "https://example.test/redirect"), new SentValidators("\"v1\"", null));
        AssertUnresolved(NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl, etag: "different"),
            prior, new SentValidators("\"v1\"", null));
        AssertUnresolved(NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl,
            lastModified: ObservedAt.AddDays(1)), prior, new SentValidators(null, ObservedAt));

        var sameAttemptPrior = Captured("attempt");
        AssertUnresolved(unchanged, sameAttemptPrior, new SentValidators("\"v1\"", null));
        var futurePrior = WithObservedAt(Captured("future"), ObservedAt.AddDays(2));
        AssertUnresolved(unchanged, futurePrior, new SentValidators("\"v1\"", null));
    }

    [Fact]
    public void LastModifiedCanBindAndMissing304ResponseValidatorsDoNotEraseSentProof()
    {
        var prior = Captured("prior");
        var withoutResponseValidators = NotModified("attempt", prior.SourceId, prior.RequestedUrl,
            prior.FinalUrl, etag: null, lastModified: null);
        var linkedByEtag = policy.Decide(withoutResponseValidators, priorCapturedAttempt: prior,
            sentValidators: new SentValidators("\"v1\"", null));
        Assert.Equal(PriorCaptureLinkStatus.Linked, linkedByEtag.PriorCaptureLinkStatus);

        var linkedByDate = policy.Decide(withoutResponseValidators, priorCapturedAttempt: prior,
            sentValidators: new SentValidators(null, ObservedAt));
        Assert.Equal(PriorCaptureLinkStatus.Linked, linkedByDate.PriorCaptureLinkStatus);
    }

    [Fact]
    public void DuplicateUnresolved304DoesNotSilentlyGainLaterPriorEvidence()
    {
        var prior = Captured("prior");
        var incoming = NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl);
        var validators = new SentValidators("\"v1\"", null);
        var unresolved = policy.Decide(incoming, sentValidators: validators);
        var duplicate = policy.Decide(NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl), unresolved,
            priorCapturedAttempt: prior, sentValidators: validators);
        Assert.Equal(ImportDisposition.DuplicateAttempt, duplicate.Disposition);
        Assert.Equal(PriorCaptureLinkStatus.Unresolved, duplicate.PriorCaptureLinkStatus);
        Assert.Null(duplicate.PriorCapturedAttempt);
    }

    [Fact]
    public void FailedAndDeferredOutcomesNeverLinkPriorCapture()
    {
        var prior = Captured("prior");
        CollectionAttemptResult[] failures =
        [
            new FailedAttemptResult("failed", prior.SourceId, prior.RequestedUrl, prior.FinalUrl,
                ObservedAt.AddMinutes(1), "network"),
            new DeferredAttemptResult("deferred", prior.SourceId, prior.RequestedUrl, prior.FinalUrl,
                ObservedAt.AddMinutes(1), "rateLimited", TimeSpan.FromSeconds(15))
        ];

        foreach (var attemptResult in failures)
        {
            var decision = policy.Decide(attemptResult, priorCapturedAttempt: prior,
                sentValidators: new SentValidators("\"v1\"", null));
            Assert.Equal(PriorCaptureLinkStatus.NotApplicable, decision.PriorCaptureLinkStatus);
            Assert.Null(decision.PriorCapturedAttempt);
            Assert.Null(decision.Capture);
        }
    }

    [Fact]
    public void OutcomeConstructorsExposeOnlyValidOutcomeData()
    {
        Assert.Throws<ArgumentException>(() => new FailedAttemptResult("a", "s", "url", "url", ObservedAt, " "));
        Assert.Throws<ArgumentException>(() => new DeferredAttemptResult("a", "s", "url", "url", ObservedAt, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeferredAttemptResult("a", "s", "url", "url", ObservedAt,
            "rateLimited", TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentException>(() => new NotModifiedAttemptResult("a", "s", "url", "url", ObservedAt,
            new CollectionResponse(200, null, null, null, [])));
        Assert.Throws<ArgumentException>(() => new CapturedAttemptResult("a", "s", "url", "url", ObservedAt,
            new CollectionResponse(304, null, null, null, []), new CaptureIdentity(new string('a', 64), 0)));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 0)]
    [InlineData("abc", 0)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", -1)]
    public void CaptureIdentityRejectsInvalidHashOrLength(string hash, long length) =>
        Assert.ThrowsAny<ArgumentException>(() => new CaptureIdentity(hash, length));

    private void AssertUnresolved(NotModifiedAttemptResult incoming, CapturedAttemptResult prior, SentValidators validators)
    {
        var decision = policy.Decide(incoming, priorCapturedAttempt: prior, sentValidators: validators);
        Assert.Equal(PriorCaptureLinkStatus.Unresolved, decision.PriorCaptureLinkStatus);
        Assert.Null(decision.PriorCapturedAttempt);
    }

    private static CapturedAttemptResult Captured(string attempt, string source = "source",
        string requested = "https://example.test/page", string final = "https://example.test/page") =>
        new(attempt, source, requested, final, ObservedAt,
            new CollectionResponse(200, "\"v1\"", ObservedAt, "text/html", ["br", "gzip"]),
            new CaptureIdentity(new string('a', 64), 12));

    private static NotModifiedAttemptResult NotModified(string attempt, string source, string requested, string final,
        string? etag = "\"v1\"", DateTimeOffset? lastModified = null) =>
        new(attempt, source, requested, final, ObservedAt.AddMinutes(1),
            new CollectionResponse(304, etag, lastModified, null, []));

    private static CapturedAttemptResult WithObservedAt(CapturedAttemptResult attemptResult, DateTimeOffset observedAt) =>
        new(attemptResult.AttemptId, attemptResult.SourceId, attemptResult.RequestedUrl, attemptResult.FinalUrl, observedAt,
            attemptResult.Response, attemptResult.Capture);
}
