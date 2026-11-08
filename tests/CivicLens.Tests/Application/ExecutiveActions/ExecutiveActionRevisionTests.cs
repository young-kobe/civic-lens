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

    private static FederalRegisterEvidence Evidence(string json, string extractionId = ExtractionId, string? dispositionNotes = null)
    {
        var document = FederalRegisterFixtures.Document(json);
        if (dispositionNotes is not null) document = document with { DispositionNotes = dispositionNotes };
        return new FederalRegisterEvidence(document, extractionId, FederalRegisterRenderer.Render(document), "donald-trump");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASourceIdAndAPersonIdHoldAtMostTheLengthOfAConfigurationId(bool source)
    {
        var (revision, _) = Create();
        var longest = new string('a', ReviewTextRules.MaximumConfigurationIdLength);
        var tooLong = longest + "a";

        (source ? revision with { SourceId = longest } : revision with { PersonId = longest }).ValidateShape();

        Assert.Throws<ArgumentException>(() => (source ? revision with { SourceId = tooLong } : revision with { PersonId = tooLong }).ValidateShape());
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

        var next = ExecutiveActionRevisions.Add(original, Now.AddDays(1), [ExecutiveActionRevisions.NoticeOf(correction)]);

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

    [Fact]
    public void DispositionNotesBindTheCompleteMultilineBlockIncludingCrLfAndSupplementaryCharacters()
    {
        var notes = "First line\r\nSecond line \U00010400";
        var evidence = Evidence(FederalRegisterFixtures.TrumpOrder, dispositionNotes: notes);
        var revision = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, evidence, []);
        var fact = Assert.Single(revision.OriginalFacts.Skip(revision.Facts.Length));
        var start = evidence.ExtractionText.IndexOf("\n" + fact.Text, StringComparison.Ordinal) + 1;

        Assert.Equal(notes, fact.Value);
        Assert.Equal((start, fact.Text.Length), (fact.Citation.Start, fact.Citation.Length));
        Assert.Equal(fact.Text, evidence.ExtractionText.Substring(fact.Citation.Start, fact.Citation.Length));
        Assert.Equal(fact.Citation, Assert.Single(revision.Citations.Skip(revision.Facts.Length)));
        Bound(revision, evidence.ExtractionText).ValidateForApproval(Extractions(revision, evidence.ExtractionText));
    }

    [Fact]
    public void DispositionNotesCanBeAbsentOrEmpty()
    {
        var absent = Create().Revision;
        var emptyEvidence = Evidence(FederalRegisterFixtures.TrumpOrder, dispositionNotes: string.Empty);
        var empty = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, emptyEvidence, []);

        Assert.Null(absent.DispositionNotes);
        Assert.Equal(string.Empty, empty.DispositionNotes!.Value);
        empty.ValidateShape();
        Bound(empty, emptyEvidence.ExtractionText).ValidateAgainst(Extractions(empty, emptyEvidence.ExtractionText));
    }

    [Fact]
    public void DispositionNotesRejectNulAndBlocksOverTheExtractionLimit()
    {
        var evidence = Evidence(FederalRegisterFixtures.TrumpOrder, dispositionNotes: "A note");
        var revision = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, evidence, []);
        var notes = revision.DispositionNotes!;

        Assert.Throws<ArgumentException>(() => (revision with { DispositionNotes = notes with { Value = "A\0note" } }).ValidateShape());
        Assert.Throws<ArgumentException>(() => ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now,
            Evidence(FederalRegisterFixtures.TrumpOrder, dispositionNotes: new string('a', DocumentExtraction.MaximumTextLength)), []));
    }

    [Fact]
    public void DispositionNotesRequireTheirOriginalRenderedSourceBlockAndExactSavedQuote()
    {
        var evidence = Evidence(FederalRegisterFixtures.TrumpOrder, dispositionNotes: "A note\r\ncontinued");
        var revision = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, evidence, []);

        Assert.Throws<ArgumentException>(() => ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now,
            evidence with { ExtractionText = evidence.ExtractionText.Replace("A note\r\ncontinued", "changed", StringComparison.Ordinal) }, []));
        Assert.Throws<ArgumentException>(() => ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now,
            evidence with { ExtractionText = evidence.ExtractionText + "\nextra" }, []));
        var changedText = evidence.ExtractionText.Replace("continued", "changed", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Bound(revision, changedText).ValidateAgainst(Extractions(revision, changedText)));
        Bound(revision, evidence.ExtractionText).ValidateAgainst(Extractions(revision, evidence.ExtractionText));
    }

    [Fact]
    public void DispositionNotesRejectWrongLabelSourceCitationAndExtraction()
    {
        var evidence = Evidence(FederalRegisterFixtures.TrumpOrder, dispositionNotes: "A note");
        var revision = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, evidence, []);
        var notes = revision.DispositionNotes!;

        Assert.Throws<ArgumentException>(() => (revision with { DispositionNotes = notes with { Label = "Other" } }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (revision with { DispositionNotes = notes with { Value = null! } }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (revision with
        {
            DispositionNotes = notes with
            { Citation = notes.Citation with { ExtractionId = new string('f', 64) } }
        }).ValidateShape());
        Assert.Throws<ArgumentException>(() => Bound(revision with
        {
            DispositionNotes = notes with
            { Citation = notes.Citation with { Start = notes.Citation.Start + 1 } }
        }, evidence.ExtractionText)
            .ValidateAgainst(Extractions(revision, evidence.ExtractionText)));
        Assert.Throws<ArgumentException>(() => Bound(revision with { DispositionNotes = notes with { Value = notes.Value + " changed" } }, evidence.ExtractionText)
            .ValidateAgainst(Extractions(revision, evidence.ExtractionText)));
    }

    [Fact]
    public void CorrectionRevisionPreservesTheOriginalDispositionNotesAndCitation()
    {
        var evidence = Evidence(FederalRegisterFixtures.BidenProclamation, dispositionNotes: "Original note\r\nsecond line");
        var original = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, evidence, []);
        var correction = Evidence(FederalRegisterFixtures.BidenProclamationCorrection, new string('c', 64), "Correction note");

        var next = ExecutiveActionRevisions.Add(original, Now.AddDays(1), [ExecutiveActionRevisions.NoticeOf(correction)]);

        Assert.Equal(original.DispositionNotes, next.DispositionNotes);
        Assert.Equal(original.DispositionNotes!.Citation, next.DispositionNotes!.Citation);
        Assert.Equal(evidence.ExtractionId, next.DispositionNotes.Citation.ExtractionId);
    }

    [Fact]
    public void ANoticeMustEqualTheTextOfItsCitationInTheSavedExtraction()
    {
        var original = Evidence(FederalRegisterFixtures.BidenProclamation);
        var correction = Evidence(FederalRegisterFixtures.BidenProclamationCorrection, new string('c', 64));
        var revision = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, original, [ExecutiveActionRevisions.NoticeOf(correction)]);
        Dictionary<string, DocumentExtraction> Saved(string correctionText) => new()
        {
            [original.ExtractionId] = new DocumentExtraction(Captured(), "federal-register-document-v1", original.ExtractionText),
            [correction.ExtractionId] = new DocumentExtraction(Captured(), "federal-register-document-v1", correctionText)
        };

        revision.ValidateAgainst(Saved(correction.ExtractionText));
        Assert.Throws<ArgumentException>(() => revision.ValidateAgainst(Saved(correction.ExtractionText.Replace("R1-2024-26714", "R1-2024-99999"))));
    }

    private static ExecutiveActionRevision Bound(ExecutiveActionRevision revision, string text)
    {
        var id = new DocumentExtraction(Captured(), "federal-register-document-v1", text).ExtractionId;
        return revision with
        {
            Facts = [.. revision.Facts.Select(fact => fact with { Citation = fact.Citation with { ExtractionId = id } })],
            DispositionNotes = revision.DispositionNotes is { } notes
                ? notes with { Citation = notes.Citation with { ExtractionId = id } }
                : null
        };
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
