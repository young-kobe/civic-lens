using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Core.Review;

public sealed class DocumentChangeRevisionComparisonTests
{
    private static readonly SourceSpan First = new(new string('a', 64), 0, 5);
    private static readonly SourceSpan Second = new(new string('a', 64), 10, 5);

    private static DocumentChangeDraftRevision Original() => new(new string('d', 32), 1, new string('c', 64),
        ReviewAuthor.AnalysisSubject, DateTimeOffset.UnixEpoch, "Headline", "Summary", "Significance", "Limits", "Institution",
        new DateOnly(2026, 10, 3), First, ["official-a", "official-b"], ["issue-a", "issue-b"], [First, Second]);

    public static TheoryData<string, DocumentChangeField> Edits => new()
    {
        { "headline", DocumentChangeField.Headline },
        { "summary", DocumentChangeField.Summary },
        { "significance", DocumentChangeField.Significance },
        { "limits", DocumentChangeField.Limits },
        { "institution", DocumentChangeField.Institution },
        { "date", DocumentChangeField.ChangeDate },
        { "dateEvidence", DocumentChangeField.ChangeDate },
        { "officials", DocumentChangeField.OfficialIds },
        { "issues", DocumentChangeField.IssueIds },
        { "citations", DocumentChangeField.Citations }
    };

    [Theory]
    [MemberData(nameof(Edits))]
    public void EachEditedFieldIsReportedAloneSoReviewerEditsCanBeCountedByField(string edit, DocumentChangeField expected)
    {
        var original = Original();
        var current = edit switch
        {
            "headline" => original with { Headline = "Changed" },
            "summary" => original with { Summary = "Changed" },
            "significance" => original with { Significance = null },
            "limits" => original with { Limits = "Changed" },
            "institution" => original with { Institution = "Changed" },
            "date" => original with { ChangeDate = new DateOnly(2026, 10, 4) },
            "dateEvidence" => original with { ChangeDateEvidence = Second },
            "officials" => original with { OfficialIds = ["official-a"] },
            "issues" => original with { IssueIds = ["issue-a", "issue-c"] },
            _ => original with { Citations = [First] }
        };

        Assert.Equal([expected], DocumentChangeRevisionComparison.ChangedFields(original, current).ToArray());
    }

    [Fact]
    public void AResaveWithIdenticalValuesIsUnchangedEvenThoughItIsANewRevision()
    {
        var original = Original();
        var resaved = original with { RevisionNumber = 2, AuthorSubject = "auth0|owner", CreatedAtUtc = DateTimeOffset.UtcNow };

        Assert.Empty(DocumentChangeRevisionComparison.ChangedFields(original, resaved));
    }

    [Fact]
    public void ReorderingSelectionsAndCitationsIsNotAnEditBecauseTheyAreSets()
    {
        var original = Original();
        var reordered = original with
        {
            OfficialIds = ["official-b", "official-a"],
            IssueIds = ["issue-b", "issue-a"],
            Citations = [Second, First]
        };

        Assert.Empty(DocumentChangeRevisionComparison.ChangedFields(original, reordered));
    }

    [Fact]
    public void SeveralEditsAreAllReportedInFieldOrder()
    {
        var original = Original();
        var current = original with { Summary = "Changed", Citations = [First], Headline = "Changed" };

        Assert.Equal([DocumentChangeField.Headline, DocumentChangeField.Summary, DocumentChangeField.Citations],
            DocumentChangeRevisionComparison.ChangedFields(original, current).ToArray());
    }
}
