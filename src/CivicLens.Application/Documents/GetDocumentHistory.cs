using System.Collections.Immutable;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Documents;

public sealed class GetDocumentHistory(IDocumentHistoryStore store)
{
    public const int DefaultMaximumObservations = 1000;
    public const int MaximumObservations = 1000;
    public const long MaximumMaterializedTextLength = 10_000_000;

    public async Task<DocumentHistory> ExecuteAsync(string sourceId, string requestedUrl,
        int maximumObservations = DefaultMaximumObservations, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedUrl);
        if (maximumObservations is < 1 or > MaximumObservations)
            throw new ArgumentOutOfRangeException(nameof(maximumObservations),
                $"History limit must be between 1 and {MaximumObservations}.");
        cancellationToken.ThrowIfCancellationRequested();

        var observations = await store.GetAsync(sourceId, requestedUrl, maximumObservations, cancellationToken);
        if (observations.Count > maximumObservations)
            throw new DocumentHistoryLimitException();

        long textLength = 0;
        DateTimeOffset? previousObservedAt = null;
        string? previousAttemptId = null;
        var attemptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.Attempt is null || observation.Extractions.IsDefault)
                throw new InvalidOperationException("Document history contains an invalid observation.");
            var attempt = observation.Attempt.AttemptResult;
            if (attempt.SourceId != sourceId || attempt.RequestedUrl != requestedUrl || !attemptIds.Add(attempt.AttemptId))
                throw new InvalidOperationException("Document history contains duplicate or out-of-scope evidence.");
            if (previousObservedAt is { } priorTime &&
                (attempt.ObservedAt < priorTime || attempt.ObservedAt == priorTime &&
                    string.CompareOrdinal(attempt.AttemptId, previousAttemptId) < 0))
                throw new InvalidOperationException("Document history observations are not chronologically ordered.");
            previousObservedAt = attempt.ObservedAt;
            previousAttemptId = attempt.AttemptId;

            var expectedSourceAttempt = attempt switch
            {
                CapturedAttemptResult captured => captured,
                NotModifiedAttemptResult when observation.Attempt.PriorCapturedAttempt is { } prior &&
                    prior.SourceId == sourceId && prior.RequestedUrl == requestedUrl => prior,
                _ => null
            };
            if (expectedSourceAttempt is null && !observation.Extractions.IsEmpty ||
                observation.Extractions.Any(extraction => !extraction.SourceAttempt.Equals(expectedSourceAttempt)))
                throw new InvalidOperationException("Document history extraction provenance does not match its observation.");
            var extractionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var extraction in observation.Extractions)
            {
                if (!extractionIds.Add(extraction.ExtractionId))
                    throw new InvalidOperationException("Document history contains duplicate extractions.");
                textLength += extraction.Text.Length;
                if (textLength > MaximumMaterializedTextLength)
                    throw new DocumentHistoryLimitException();
            }
        }

        var streams = new List<StreamBuilder>();
        var streamsByKey = new Dictionary<StreamKey, StreamBuilder>();
        var previouslyActiveKeys = new HashSet<StreamKey>();
        foreach (var observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.Extractions.IsDefaultOrEmpty)
            {
                foreach (var key in previouslyActiveKeys) streamsByKey[key].Break();
                previouslyActiveKeys.Clear();
                continue;
            }

            var currentKeys = new HashSet<StreamKey>();
            foreach (var extraction in observation.Extractions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = new StreamKey(extraction.ParserVersion, extraction.NormalizationVersion,
                    extraction.Profile?.RevisionId);
                currentKeys.Add(key);
                if (!streamsByKey.TryGetValue(key, out var stream))
                {
                    stream = new StreamBuilder(key);
                    streams.Add(stream);
                    streamsByKey.Add(key, stream);
                }
                stream.Add(observation, extraction);
            }

            foreach (var key in previouslyActiveKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!currentKeys.Contains(key))
                    streamsByKey[key].Break();
            }
            previouslyActiveKeys = currentKeys;
        }

        return new DocumentHistory(observations.ToImmutableArray(), streams.Select(stream => stream.Build()).ToImmutableArray());
    }

    private readonly record struct StreamKey(string ParserVersion, string NormalizationVersion, string? ProfileRevisionId);

    private sealed class StreamBuilder(StreamKey key)
    {
        private readonly List<TransitionBuilder> transitions = [];
        private bool hasAdjacentObservation;
        public StreamKey Key { get; } = key;

        public void Add(DocumentHistoryObservation observation, CivicLens.Core.Documents.DocumentExtraction extraction)
        {
            if (!hasAdjacentObservation || transitions.Count == 0 || transitions[^1].TextSha256 != extraction.TextSha256 ||
                transitions[^1].Text != extraction.Text)
            {
                transitions.Add(new TransitionBuilder(extraction.TextSha256, extraction.Text,
                    observation.Attempt.AttemptResult.AttemptId, extraction.ExtractionId));
            }
            else
            {
                transitions[^1].Add(observation.Attempt.AttemptResult.AttemptId, extraction.ExtractionId);
            }
            hasAdjacentObservation = true;
        }

        public void Break() => hasAdjacentObservation = false;

        public DocumentHistoryStream Build() => new(Key.ParserVersion, Key.NormalizationVersion,
            Key.ProfileRevisionId, transitions.Select(transition => transition.Build()).ToImmutableArray());
    }

    private sealed class TransitionBuilder(string textSha256, string text, string attemptId, string extractionId)
    {
        private readonly List<string> attemptIds = [attemptId];
        private readonly List<string> extractionIds = [extractionId];
        public string TextSha256 { get; } = textSha256;
        public string Text { get; } = text;

        public void Add(string nextAttemptId, string nextExtractionId)
        {
            attemptIds.Add(nextAttemptId);
            extractionIds.Add(nextExtractionId);
        }

        public DocumentHistoryTransition Build() => new(TextSha256, Text,
            attemptIds.ToImmutableArray(), extractionIds.ToImmutableArray());
    }
}
