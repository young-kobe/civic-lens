using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.ExecutiveActions;

public sealed class ExecutiveActionRevisionTests
{
    private const string ExtractionId = "e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0";
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static (ExecutiveActionRevision Revision, string Text) Create(string json = FederalRegisterFixtures.TrumpOrder)
    {
        var evidence = Evidence(json);
        return (ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, evidence, []), evidence.ExtractionText);
    }

    private static FederalRegisterEvidence Evidence(string json, string extractionId = ExtractionId)
    {
        var document = FederalRegisterFixtures.Document(json);
        return new FederalRegisterEvidence(document, extractionId, FederalRegisterRenderer.Render(document), "donald-trump");
    }

    [Fact]
    public void EachFactCitesItsOwnLineOfTheExtractionAndTheCodeIsTheAuthor()
    {
        var (revision, text) = Create();

        revision.ValidateShape();
        Assert.Equal(ReviewAuthor.CodeSubject, revision.AuthorSubject);
        Assert.Equal("donald-trump", Assert.Single(revision.OfficialIds()));
        Assert.All(revision.Facts, fact => Assert.Equal(fact.Text, text.Substring(fact.Citation.Start, fact.Citation.Length)));
        Assert.Equal(text.Split('\n').Length, revision.Facts.Length);
    }

    [Fact]
    public void ACorrectionAddsANoticeThatCitesItsOwnExtractionAndLeavesTheOriginalFactsAsTheyWere()
    {
        var original = Create(FederalRegisterFixtures.BidenProclamation).Revision;
        var correction = Evidence(FederalRegisterFixtures.BidenProclamationCorrection, new string('c', 64));

        var next = ExecutiveActionRevisions.Add(original, Now.AddDays(1), ExecutiveActionRevisions.NoticeOf(correction));

        Assert.Equal(original.RecordId, next.RecordId);
        Assert.Equal(2, next.RevisionNumber);
        Assert.Equal(original.Facts, next.Facts);
        Assert.Equal("2024-26714", next.DocumentNumber);
        Assert.Equal(new DateOnly(2024, 11, 13), next.PublishedOn);
        var notice = Assert.Single(next.Notices);
        Assert.Equal(("R1-2024-26714", new DateOnly(2024, 11, 20), new string('c', 64)), (notice.DocumentNumber, notice.PublishedOn, notice.Citation.ExtractionId));
        Assert.Equal(notice.Text, correction.ExtractionText.Substring(notice.Citation.Start, notice.Citation.Length));
        next.ValidateShape();
    }

    [Fact]
    public void ARecordThatStartsFromTheOriginalHoldsTheNoticesThatWaitedInDateOrder()
    {
        var evidence = Evidence(FederalRegisterFixtures.BidenProclamation);
        var later = ExecutiveActionRevisions.NoticeOf(Evidence(FederalRegisterFixtures.BidenProclamationCorrection, new string('c', 64)));
        var earlier = later with { DocumentNumber = "R1-2024-26700", PublishedOn = new DateOnly(2024, 11, 14) };

        var revision = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, evidence, [later, earlier]);

        Assert.Equal(["R1-2024-26700", "R1-2024-26714"], revision.Notices.Select(notice => notice.DocumentNumber));
        Assert.Equal(1, revision.RevisionNumber);
    }

    [Fact]
    public void AFactThatDiffersFromTheTypedFieldIsRefusedSoTheTitleAndItsCitationCannotDrift()
    {
        var (revision, _) = Create();
        var drifted = revision with { Title = "A different title" };

        Assert.Throws<ArgumentException>(drifted.ValidateShape);
    }

    [Fact]
    public void AFactMustEqualTheTextOfItsCitationInTheSavedExtraction()
    {
        var (revision, text) = Create();

        Assert.Throws<ArgumentException>(() => Bound(revision, text.Replace("14147", "14148")).ValidateAgainst(Extractions(revision, text.Replace("14147", "14148"))));
        Bound(revision, text).ValidateForApproval(Extractions(revision, text));
    }

    private static ExecutiveActionRevision Bound(ExecutiveActionRevision revision, string text)
    {
        var id = new DocumentExtraction(Captured(), "federal-register-document-v1", text).ExtractionId;
        return revision with { Facts = [.. revision.Facts.Select(fact => fact with { Citation = fact.Citation with { ExtractionId = id } })] };
    }

    private static Dictionary<string, DocumentExtraction> Extractions(ExecutiveActionRevision revision, string text)
    {
        var extraction = new DocumentExtraction(Captured(), "federal-register-document-v1", text);
        return new() { [extraction.ExtractionId] = extraction };
    }

    [Fact]
    public void ARevisionByAnyoneButTheCodeIsRefusedBecauseNoPersonEditsTheseRecords()
    {
        var (revision, _) = Create();

        Assert.Throws<ArgumentException>((revision with { AuthorSubject = "auth0|someone" }).ValidateShape);
    }

    private static CapturedAttemptResult Captured() => new("attempt", "source", "https://example.test/a", "https://example.test/a",
        DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "application/json", []), new CaptureIdentity(new string('a', 64), 0));
}
