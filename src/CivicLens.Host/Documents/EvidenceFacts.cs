using CivicLens.Core.Documents;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Ui;

namespace CivicLens.Host.Documents;

public static class EvidenceFacts
{
    public static IReadOnlyList<Fact> For(DocumentExtraction extraction) =>
    [
        new("Observed", Moment.Full(extraction.SourceAttempt.ObservedAt)),
        new("URL", extraction.SourceAttempt.RequestedUrl),
        new("Parser", extraction.ParserVersion),
        new("Normalization", extraction.NormalizationVersion),
        new("Content profile", extraction.Profile is { } profile ? $"{profile.Id}, revision {profile.RevisionId[..8]}" : "None")
    ];

    public static IReadOnlyList<(string Label, string Value)> Identifiers(DocumentExtraction extraction) =>
    [
        ("Extraction ID", extraction.ExtractionId),
        ("Source ID", extraction.SourceAttempt.SourceId),
        ("Attempt ID", extraction.SourceAttempt.AttemptId),
        ("Final URL", extraction.SourceAttempt.FinalUrl),
        ("Capture SHA-256", extraction.SourceAttempt.Capture.Sha256),
        ("Text SHA-256", extraction.TextSha256),
        ("Text length", $"{extraction.Text.Length} UTF-16 units")
    ];
}
