using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Core.Relationships;

public sealed record RelationshipRevision(string RecordId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    RelationshipCitation Statement, RelationshipCitation Target, RelationshipMatch Match, RelationshipType Type, string Reason,
    SourceSpan StatementQuote, string TargetFactId) : IReviewRevision
{
    public const int MaximumReasonLength = 400;
    public const int MaximumFactIdLength = 16;

    public void ValidateShape()
    {
        if (!LowercaseHex.Is(RecordId, 64))
            throw new ArgumentException("A relationship revision needs a valid record ID.");
        if (!ReviewAuthor.IsValidRevisionHeader(RevisionNumber, AuthorSubject, CreatedAtUtc))
            throw new ArgumentException("A relationship revision needs a number, an author, and a UTC creation time.");
        ValidateCitations();
        ValidateSpans();
        if (!Enum.IsDefined(Type) || !RelationshipTypes.For(Target.Kind).Contains(Type))
            throw new ArgumentException("The relationship type does not fit the kind of the target.");
        ReviewTextRules.ValidateText(Reason, MaximumReasonLength, nameof(Reason));
        if (Reason.Contains('\n') || Reason.Contains('\r'))
            throw new ArgumentException("The reason is one sentence on one line.", nameof(Reason));
        if (string.IsNullOrWhiteSpace(TargetFactId) || TargetFactId.Length > MaximumFactIdLength)
            throw new ArgumentException("A relationship names one fact of its target.", nameof(TargetFactId));
    }

    public void ValidateForApproval() => ValidateShape();

    public void ValidateAsAnalysisRevision(RelationshipRevision? previous)
    {
        ValidateShape();
        if (ReviewAuthor.KindOf(AuthorSubject) != ReviewAuthorKind.Analysis || RevisionNumber != (previous?.RevisionNumber ?? 0) + 1)
            throw new ArgumentException("An analysis revision follows the current revision and has the analysis author.");
        if (previous is not null && previous.RecordId != RecordId)
            throw new ArgumentException("A revision belongs to the record of the revision before it.");
    }

    public void ValidatePersonEditOf(RelationshipRevision previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ValidateShape();
        if (ReviewAuthor.KindOf(AuthorSubject) != ReviewAuthorKind.Person || RevisionNumber != previous.RevisionNumber + 1)
            throw new ArgumentException("A person revision follows the current revision.");
        if (RecordId != previous.RecordId || Statement != previous.Statement || Target != previous.Target || Match != previous.Match)
            throw new ArgumentException("A person edits only the type, the reason, the quote, and the fact.");
    }

    public bool IsPersonRevisionCiting(RelationshipCitation statement, RelationshipCitation target) =>
        ReviewAuthor.KindOf(AuthorSubject) == ReviewAuthorKind.Person && Statement == statement && Target == target;

    private void ValidateCitations()
    {
        if (Statement is not { Kind: RelationshipRecordKind.Statement } || Target is not { Kind: RelationshipRecordKind.Action or RelationshipRecordKind.Vote })
            throw new ArgumentException("A relationship cites one statement and one action or vote.");
        foreach (var citation in new[] { Statement, Target })
            if (!LowercaseHex.Is(citation.RecordId, 64) || citation.RevisionNumber < 1)
                throw new ArgumentException("A relationship citation needs a record ID and a revision number.");
    }

    private void ValidateSpans()
    {
        if (!IsBounded(Match.Span) || !IsBounded(StatementQuote) || StatementQuote.Length > StatementDraftRevision.MaximumQuotationLength)
            throw new ArgumentException("The matched span and the quote need bounded spans.");
        if (!Enum.IsDefined(Match.Basis) || !StatementQuote.Covers(Match.Span))
            throw new ArgumentException("The quote must cover the matched span in the same extraction.");
    }

    private static bool IsBounded(SourceSpan span) => span is { Start: >= 0, Length: > 0 } && LowercaseHex.Is(span.ExtractionId, 64);
}
