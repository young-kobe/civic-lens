using CivicLens.Core.Review;

namespace CivicLens.Tests.Core.Review;

public sealed class DocumentChangeDraftRevisionTests
{
    [Fact]
    public void AnAiProposedChangeDateNeedsAHumanRevisionBeforeApproval()
    {
        var citation = new DocumentChangeCitation(new string('e', 64), 0, 4);
        var dated = Revision(new string('d', 32), 1) with
        {
            AuthorSubject = ReviewAuthor.AnalysisSubject,
            ChangeDate = new DateOnly(2026, 10, 3),
            ChangeDateEvidence = citation,
            Citations = [citation]
        };

        Assert.Throws<ArgumentException>(dated.ValidateForApproval);
        (dated with { RevisionNumber = 2, AuthorSubject = "auth0|owner" }).ValidateForApproval();
        (dated with { ChangeDate = null, ChangeDateEvidence = null }).ValidateForApproval();
    }

    private static DocumentChangeDraftRevision Revision(string draftId, int number) =>
        new(draftId, number, new string('c', 64), "reviewer", DateTimeOffset.UnixEpoch,
            "Headline", "Summary", null, null, "Institution", null, null, [], [], []);
}
