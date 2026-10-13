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

    [Fact]
    public void AStoredRevisionMustKeepItsIdentityTextBoundsAndEvidenceBindings()
    {
        var citation = new DocumentChangeCitation(new string('e', 64), 0, 4);
        var valid = Revision(new string('d', 32), 1) with { Citations = [citation] };
        valid.ValidateShape();

        Assert.Throws<ArgumentException>((valid with { DraftId = new string('D', 32) }).ValidateShape);
        Assert.Throws<ArgumentException>((valid with { RevisionNumber = 0 }).ValidateShape);
        Assert.Throws<ArgumentException>((valid with { ComparisonId = "short" }).ValidateShape);
        Assert.Throws<ArgumentException>((valid with { CreatedAtUtc = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) }).ValidateShape);
        Assert.Throws<ArgumentException>((valid with { Headline = new string('h', DocumentChangeDraftRevision.MaximumHeadlineLength + 1) }).ValidateShape);
        Assert.Throws<ArgumentException>((valid with { Summary = "bad\0text" }).ValidateShape);
        Assert.Throws<ArgumentException>((valid with { OfficialIds = ["a", "a"] }).ValidateShape);
        Assert.Throws<ArgumentException>((valid with { ChangeDate = new DateOnly(2026, 10, 3) }).ValidateShape);
        Assert.Throws<ArgumentException>((valid with { Citations = [citation, citation] }).ValidateShape);
        Assert.Throws<ArgumentException>((valid with { Citations = [new DocumentChangeCitation(new string('e', 64), 0, 0)] }).ValidateShape);
    }

    private static DocumentChangeDraftRevision Revision(string draftId, int number) =>
        new(draftId, number, new string('c', 64), "reviewer", DateTimeOffset.UnixEpoch,
            "Headline", "Summary", null, null, "Institution", null, null, [], [], []);
}
