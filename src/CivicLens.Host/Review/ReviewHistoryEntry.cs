using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Host.Review;

public sealed record ReviewHistoryEntry(DateTimeOffset When, string Event, ReviewDecisionKind? Decision, string ActorSubject, string? Note,
    string? CorrectionNote = null)
{
    public static ReviewHistoryEntry ForRevision(DateTimeOffset createdAtUtc, int revisionNumber, string authorSubject, string headline) =>
        new(createdAtUtc, revisionNumber > 1 ? $"Revision {revisionNumber} saved"
                : ReviewAuthor.IsAnalysis(authorSubject) ? "AI draft written"
                : ReviewAuthor.IsCode(authorSubject) ? "Code draft written" : "Draft started", null,
            authorSubject, DraftStatusDisplay.DraftName(headline));

    public static ReviewHistoryEntry ForDecision(ReviewDecision decision) =>
        new(decision.CreatedAtUtc, $"on revision {decision.RevisionNumber}", decision.Kind, decision.ActorSubject, NoteOf(decision), decision.CorrectionNote);

    public static IReadOnlyList<ReviewHistoryEntry> Newest(IEnumerable<ReviewHistoryEntry> revisions, ImmutableArray<ReviewDecision> decisions) =>
        [.. revisions.Concat(decisions.Select(ForDecision)).OrderByDescending(entry => entry.When)];

    private static string? NoteOf(ReviewDecision decision) => decision.ResolvedDecisionIds.Length switch
    {
        0 => decision.Note,
        1 => $"{decision.Note} Resolved 1 concern.",
        var count => $"{decision.Note} Resolved {count} concerns."
    };
}
