using System.Collections.Immutable;
using CivicLens.Core.Collection;

namespace CivicLens.Tests.Core.Collection;

public sealed class ObservationImportPolicyTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private readonly ObservationImportPolicy policy = new();

    [Fact]
    public void EqualSeparateInstanceReplaysAndPreservesResolved304Link()
    {
        var prior = Captured("prior");
        var incoming = NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl);
        var validators = new SentValidators("\"v1\"", null);
        var persisted = policy.Decide(incoming, priorCapturedObservation: prior, sentValidators: validators);

        var equalInput = NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl);
        Assert.Equal(incoming, equalInput);
        var replay = policy.Decide(equalInput, persisted, sentValidators: validators);

        Assert.Equal(ObservationImportTransition.Replay, replay.Transition);
        Assert.Equal(RepresentationBinding.Linked, replay.RepresentationBinding);
        Assert.Same(prior, replay.CapturedRepresentation);
        Assert.Throws<InvalidOperationException>(() => policy.Decide(incoming, persisted,
            sentValidators: new SentValidators("\"different\"", null)));
    }

    [Fact]
    public void ReplayConflictsOnResponseMetadataIncludingEncodingOrder()
    {
        var captured = Captured("attempt");
        var persisted = policy.Decide(captured);
        var reordered = new CapturedObservation("attempt", captured.SourceId, captured.RequestedUrl, captured.FinalUrl,
            captured.ObservedAt, new ObservationResponse(200, "\"v1\"", ObservedAt, "text/html", ["gzip", "br"]),
            captured.Capture);
        Assert.Throws<InvalidOperationException>(() => policy.Decide(reordered, persisted));

        var otherOutcome = new FailedObservation("attempt", captured.SourceId, captured.RequestedUrl, captured.FinalUrl,
            captured.ObservedAt, "network");
        Assert.False(captured.Equals(otherOutcome));
        Assert.Throws<InvalidOperationException>(() => policy.Decide(otherOutcome, persisted));
        Assert.Throws<InvalidOperationException>(() => policy.Decide(captured, policy.Decide(Captured("other"))));
    }

    [Fact]
    public void IdenticalCaptureBytesOnDifferentAttemptsRemainSeparateObservations()
    {
        var first = policy.Decide(Captured("attempt-1"));
        var second = policy.Decide(Captured("attempt-2"));
        Assert.NotSame(first.Observation, second.Observation);
        Assert.Equal(first.Capture, second.Capture);
        Assert.Equal(ObservationImportTransition.NewObservation, second.Transition);
    }

    [Fact]
    public void ResponseEncodingsAreDefensivelySnapshottedAndComparedByValue()
    {
        var encodings = new[] { "br", "gzip" };
        var response = new ObservationResponse(200, null, null, null, encodings);
        encodings[0] = "changed";
        Assert.Equal(new[] { "br", "gzip" }, response.ContentEncodings);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)response.ContentEncodings)[0] = "changed");
        Assert.Equal(response, new ObservationResponse(200, null, null, null, ["br", "gzip"]));
        Assert.Empty(new ObservationResponse(200, null, null, null, Array.Empty<string>()).ContentEncodings);
        Assert.Throws<ArgumentException>(() => new ObservationResponse(200, null, null, null,
            default(ImmutableArray<string>)));
    }

    [Fact]
    public void NotModifiedLinksOnlyWhenSourceUrlsAndSentValidatorBindCapturedRepresentation()
    {
        var prior = Captured("prior");
        var unchanged = NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl);
        var decision = policy.Decide(unchanged, priorCapturedObservation: prior,
            sentValidators: new SentValidators("\"v1\"", null));
        Assert.Equal(RepresentationBinding.Linked, decision.RepresentationBinding);
        Assert.Same(prior, decision.CapturedRepresentation);
        Assert.Equal(304, decision.Observation.Response!.StatusCode);
        Assert.Equal("\"v1\"", decision.Observation.Response.ETag);
        Assert.Null(decision.Observation.Response.ContentType);

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
        var linkedByEtag = policy.Decide(withoutResponseValidators, priorCapturedObservation: prior,
            sentValidators: new SentValidators("\"v1\"", null));
        Assert.Equal(RepresentationBinding.Linked, linkedByEtag.RepresentationBinding);

        var linkedByDate = policy.Decide(withoutResponseValidators, priorCapturedObservation: prior,
            sentValidators: new SentValidators(null, ObservedAt));
        Assert.Equal(RepresentationBinding.Linked, linkedByDate.RepresentationBinding);
    }

    [Fact]
    public void ReplayOfUnresolved304DoesNotSilentlyGainLaterPriorEvidence()
    {
        var prior = Captured("prior");
        var incoming = NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl);
        var validators = new SentValidators("\"v1\"", null);
        var unresolved = policy.Decide(incoming, sentValidators: validators);
        var replay = policy.Decide(NotModified("attempt", prior.SourceId, prior.RequestedUrl, prior.FinalUrl), unresolved,
            priorCapturedObservation: prior, sentValidators: validators);
        Assert.Equal(ObservationImportTransition.Replay, replay.Transition);
        Assert.Equal(RepresentationBinding.Unresolved, replay.RepresentationBinding);
        Assert.Null(replay.CapturedRepresentation);
    }

    [Fact]
    public void FailedAndDeferredOutcomesNeverLinkPriorCapture()
    {
        var prior = Captured("prior");
        Observation[] failures =
        [
            new FailedObservation("failed", prior.SourceId, prior.RequestedUrl, prior.FinalUrl,
                ObservedAt.AddMinutes(1), "network"),
            new DeferredObservation("deferred", prior.SourceId, prior.RequestedUrl, prior.FinalUrl,
                ObservedAt.AddMinutes(1), "rateLimited", TimeSpan.FromSeconds(15))
        ];

        foreach (var observation in failures)
        {
            var decision = policy.Decide(observation, priorCapturedObservation: prior,
                sentValidators: new SentValidators("\"v1\"", null));
            Assert.Equal(RepresentationBinding.NotApplicable, decision.RepresentationBinding);
            Assert.Null(decision.CapturedRepresentation);
            Assert.Null(decision.Capture);
        }
    }

    [Fact]
    public void OutcomeConstructorsExposeOnlyValidOutcomeData()
    {
        Assert.Throws<ArgumentException>(() => new FailedObservation("a", "s", "url", "url", ObservedAt, " "));
        Assert.Throws<ArgumentException>(() => new DeferredObservation("a", "s", "url", "url", ObservedAt, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeferredObservation("a", "s", "url", "url", ObservedAt,
            "rateLimited", TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentException>(() => new NotModifiedObservation("a", "s", "url", "url", ObservedAt,
            new ObservationResponse(200, null, null, null, [])));
        Assert.Throws<ArgumentException>(() => new CapturedObservation("a", "s", "url", "url", ObservedAt,
            new ObservationResponse(304, null, null, null, []), new CaptureIdentity(new string('a', 64), 0)));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 0)]
    [InlineData("abc", 0)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", -1)]
    public void CaptureIdentityRejectsInvalidHashOrLength(string hash, long length) =>
        Assert.ThrowsAny<ArgumentException>(() => new CaptureIdentity(hash, length));

    private void AssertUnresolved(NotModifiedObservation incoming, CapturedObservation prior, SentValidators validators)
    {
        var decision = policy.Decide(incoming, priorCapturedObservation: prior, sentValidators: validators);
        Assert.Equal(RepresentationBinding.Unresolved, decision.RepresentationBinding);
        Assert.Null(decision.CapturedRepresentation);
    }

    private static CapturedObservation Captured(string attempt, string source = "source",
        string requested = "https://example.test/page", string final = "https://example.test/page") =>
        new(attempt, source, requested, final, ObservedAt,
            new ObservationResponse(200, "\"v1\"", ObservedAt, "text/html", ["br", "gzip"]),
            new CaptureIdentity(new string('a', 64), 12));

    private static NotModifiedObservation NotModified(string attempt, string source, string requested, string final,
        string? etag = "\"v1\"", DateTimeOffset? lastModified = null) =>
        new(attempt, source, requested, final, ObservedAt.AddMinutes(1),
            new ObservationResponse(304, etag, lastModified, null, []));

    private static CapturedObservation WithObservedAt(CapturedObservation observation, DateTimeOffset observedAt) =>
        new(observation.AttemptId, observation.SourceId, observation.RequestedUrl, observation.FinalUrl, observedAt,
            observation.Response, observation.Capture);
}
