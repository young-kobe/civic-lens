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

    private static (ExecutiveActionRevision Revision, string Text) Create(string json = FederalRegisterFixtures.TrumpOrder, int number = 1)
    {
        var document = FederalRegisterFixtures.Document(json);
        var text = FederalRegisterRenderer.Render(document);
        var evidence = new FederalRegisterEvidence(document, ExtractionId, text, "donald-trump");
        return (ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, number, Now, evidence), text);
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
    public void ARecordIdFollowsTheDocumentThatIsCorrectedSoACorrectionAppendsToTheSameRecord()
    {
        var original = Create(FederalRegisterFixtures.BidenProclamation).Revision;
        var correction = Create(FederalRegisterFixtures.BidenProclamationCorrection, number: 2).Revision;

        Assert.Equal(original.RecordId, correction.RecordId);
        Assert.Equal("2024-26714", correction.CorrectionOf);
        Assert.Equal(2, correction.RevisionNumber);
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
