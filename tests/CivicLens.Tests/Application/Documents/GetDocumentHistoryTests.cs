using System.Collections.Immutable;
using CivicLens.Application.Collection;
using CivicLens.Application.Documents;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Application.Documents;

public sealed class GetDocumentHistoryTests
{
    private const string Url = "https://example.test/document";

    [Fact]
    public async Task GroupsAdjacentEqualTextButKeepsReversionsAsSeparateTransitions()
    {
        var observations = new[]
        {
            Captured("a1", 1, "text A"), Captured("a2", 2, "text A"),
            Captured("b", 3, "text B"), Captured("a3", 4, "text A")
        };
        var history = await new GetDocumentHistory(new HistoryStore(observations)).ExecuteAsync("source", Url);
        var transitions = Assert.Single(history.Streams).Transitions;

        Assert.Equal(new[] { "text A", "text B", "text A" }, transitions.Select(item => item.Text));
        Assert.Equal(new[] { "a1", "a2" }, transitions[0].AttemptIds);
        Assert.Equal("a3", Assert.Single(transitions[2].AttemptIds));
    }

    [Fact]
    public async Task FailureAndMissingExtractionBreakAdjacencyAndSettingsHaveIndependentStreams()
    {
        var first = Captured("first", 1, "same", parser: "parser-v1");
        var failureAttempt = new StoredCollectionAttempt(new FailedAttemptResult("failed", "source", Url, Url,
            DateTimeOffset.UnixEpoch.AddDays(2), "network"), null, null);
        var sameAfterGap = Captured("after-gap", 3, "same", parser: "parser-v1");
        var changedParser = Captured("next", 4, "same", parser: "parser-v2");
        var history = await new GetDocumentHistory(new HistoryStore([first,
            new DocumentHistoryObservation(failureAttempt, ImmutableArray<DocumentExtraction>.Empty), sameAfterGap, changedParser]))
            .ExecuteAsync("source", Url);

        Assert.Equal(2, history.Streams.Length);
        Assert.Equal(2, history.Streams.Single(stream => stream.ParserVersion == "parser-v1").Transitions.Length);
        Assert.Single(history.Streams.Single(stream => stream.ParserVersion == "parser-v2").Transitions);
        Assert.Empty(history.Observations[1].Extractions);
    }

    [Fact]
    public async Task RejectsInvalidLimitBeforeReadingStore()
    {
        var store = new HistoryStore([]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new GetDocumentHistory(store).ExecuteAsync("source", Url, 1001));
        Assert.Equal(0, store.Calls);
    }

    private static DocumentHistoryObservation Captured(string id, int day, string text, string parser = "parser-v1")
    {
        var attempt = new CapturedAttemptResult(id, "source", Url, Url, DateTimeOffset.UnixEpoch.AddDays(day),
            new CollectionResponse(200, null, null, "text/plain", []), new CaptureIdentity(new string('a', 64), 4));
        var extraction = new DocumentExtraction(attempt, parser, "normalization-v1", text);
        return new DocumentHistoryObservation(new StoredCollectionAttempt(attempt, null, null),
            ImmutableArray.Create(extraction));
    }

    private sealed class HistoryStore(IReadOnlyList<DocumentHistoryObservation> observations) : IDocumentHistoryStore
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<DocumentHistoryObservation>> GetAsync(string sourceId, string requestedUrl,
            int maximumObservations, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("source", sourceId);
            Assert.Equal(Url, requestedUrl);
            return Task.FromResult(observations);
        }
    }
}
