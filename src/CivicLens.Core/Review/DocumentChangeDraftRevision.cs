using System.Collections.Immutable;

namespace CivicLens.Core.Review;

public sealed record DocumentChangeDraftRevision(string DraftId, int RevisionNumber, string ComparisonId,
    string AuthorSubject, DateTimeOffset CreatedAtUtc, string Headline, string Summary,
    string? Significance, string? Limits, string Institution,
    DateOnly? ChangeDate, DocumentChangeCitation? ChangeDateEvidence,
    ImmutableArray<string> OfficialIds, ImmutableArray<string> IssueIds,
    ImmutableArray<DocumentChangeCitation> Citations)
{
    public void ValidateForApproval()
    {
        if (string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary) ||
            string.IsNullOrWhiteSpace(Institution) || Citations.IsDefaultOrEmpty)
            throw new ArgumentException("Approval requires a headline, summary, source institution, and at least one citation.");
        if ((ChangeDate is null) != (ChangeDateEvidence is null))
            throw new ArgumentException("An actual change date requires an exact evidence citation.");
    }
}
