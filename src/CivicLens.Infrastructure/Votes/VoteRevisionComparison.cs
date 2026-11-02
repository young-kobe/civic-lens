using System.Globalization;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Owns the semantic comparison of two vote revisions. It names the fields that differ and never returns a field value, because the text can be private.</summary>
internal static class VoteRevisionComparison
{
    private const char Separator = '\u001F';

    public static IReadOnlyList<string> DifferingFields(VoteDraftRevision stored, VoteDraftRevision rebuilt) =>
        [.. Fields(stored).Zip(Fields(rebuilt)).Where(pair => pair.First.Value != pair.Second.Value).Select(pair => pair.First.Name)];

    private static IEnumerable<(string Name, string Value)> Fields(VoteDraftRevision revision)
    {
        yield return (nameof(revision.RecordId), revision.RecordId);
        yield return (nameof(revision.RevisionNumber), Text(revision.RevisionNumber));
        yield return (nameof(revision.AuthorSubject), revision.AuthorSubject);
        yield return (nameof(revision.CreatedAtUtc), Text(revision.CreatedAtUtc.UtcTicks));
        yield return (nameof(revision.Headline), revision.Headline);
        yield return (nameof(revision.Summary), revision.Summary);
        yield return (nameof(revision.TextOrigin), Text(revision.TextOrigin));
        yield return (nameof(revision.Suggestion), revision.Suggestion is { } suggestion ? Join(suggestion.Headline, suggestion.Summary, suggestion.RunId, suggestion.Fingerprint) : "");
        yield return (nameof(revision.SourceId), revision.SourceId);
        yield return (nameof(revision.Source), Text(revision.Source));
        yield return (nameof(revision.Chamber), Text(revision.Chamber));
        yield return (nameof(revision.Congress), Text(revision.Congress));
        yield return (nameof(revision.SessionNumber), Text(revision.SessionNumber));
        yield return (nameof(revision.RollCallNumber), Text(revision.RollCallNumber));
        yield return (nameof(revision.BillType), revision.BillType ?? "");
        yield return (nameof(revision.BillNumber), revision.BillNumber is { } number ? Text(number) : "");
        yield return (nameof(revision.CandidateId), revision.CandidateId);
        yield return (nameof(revision.CandidateUpdateDate), Text(revision.CandidateUpdateDate.UtcTicks));
        yield return (nameof(revision.Facts), Join(revision.Facts.Select(fact => Join(fact.Text, Span(fact.Citation)))));
        yield return (nameof(revision.VotedOn), Text(revision.VotedOn));
        yield return (nameof(revision.VotedOnCitation), Span(revision.VotedOnCitation));
        yield return (nameof(revision.Kind), Text(revision.Kind));
        yield return (nameof(revision.KindCitation), revision.KindCitation is { } kind ? Span(kind) : "");
        yield return (nameof(revision.Members), VoteMemberLists.IdOf(revision.Members));
        yield return (nameof(revision.TrackedIssueIds), Join(revision.TrackedIssueIds));
        yield return (nameof(revision.IssueCandidateIds), revision.IssueCandidateIds.IsDefault ? "unrecorded" : Join(revision.IssueCandidateIds));
        yield return (nameof(revision.IssueRevisionId), revision.IssueRevisionId);
        yield return (nameof(revision.SubjectsHash), revision.SubjectsHash ?? "");
    }

    private static string Span(SourceSpan span) => Join(span.ExtractionId, Text(span.Start), Text(span.Length));

    private static string Join(params string[] parts) => string.Join(Separator, parts);

    private static string Join(IEnumerable<string> parts) => string.Join(Separator, parts);

    private static string Text<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
}
