using CivicLens.Application.Documents;
using CivicLens.Core.Collection;
using CivicLens.Host.Components.Ui;

namespace CivicLens.Host.Documents;

public static class ObservationDisplay
{
    public static (string Label, Tone Tone) Outcome(CollectionAttemptResult attempt) => attempt switch
    {
        CapturedAttemptResult => ("Saved", Tone.Ok),
        NotModifiedAttemptResult => ("Not changed", Tone.Neutral),
        FailedAttemptResult => ("Could not save", Tone.Bad),
        DeferredAttemptResult => ("Put off", Tone.Warn),
        _ => ("Unknown", Tone.Neutral)
    };

    public static string Detail(CollectionAttemptResult attempt)
    {
        var response = attempt.Response is { } value ? $"HTTP {value.StatusCode}." : "No response.";
        return attempt switch
        {
            FailedAttemptResult failed => $"{response} Failure code {failed.FailureCode}. This is a coverage gap, not evidence of no activity.",
            DeferredAttemptResult deferred => $"{response} Deferral code {deferred.FailureCode}. This is a coverage gap, not evidence of no activity.",
            _ => response
        };
    }

    public static IReadOnlyList<(string Label, string Value)> Versions(DocumentHistory history)
    {
        var extractions = history.Observations.SelectMany(item => item.Extractions).ToArray();
        return
        [
            ("Parser", Distinct(extractions.Select(item => item.ParserVersion))),
            ("Normalization", Distinct(extractions.Select(item => item.NormalizationVersion))),
            ("Content profile", Distinct(extractions.Select(item => item.Profile is { } profile ? $"{profile.Id}, revision {profile.RevisionId[..8]}" : "None")))
        ];
    }

    private static string Distinct(IEnumerable<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
        return distinct.Length == 0 ? "No saved text yet" : string.Join(", ", distinct);
    }
}
