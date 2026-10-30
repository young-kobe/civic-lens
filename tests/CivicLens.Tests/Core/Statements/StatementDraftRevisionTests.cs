using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Tests.Core.Statements;

public sealed class StatementDraftRevisionTests
{
    private const string Text = "Senator Warren announced a rent cap. \"We act now,\" she said.";
    private static readonly string ExtractionId = new('e', 64);
    private static readonly SourceSpan Span = new(ExtractionId, 0, 14);
    private static readonly DateOnly Date = new(2026, 10, 3);

    private static StatementDraftRevision Revision(string author = "reviewer") =>
        new(new string('c', 64), 1, author, DateTimeOffset.UnixEpoch, "Headline", "Summary", null, null, ["warren"], [], [], [],
            [new(Span, StatementQuotationKind.ReleaseText)], []);

    // The review lists sort by this date, and a statement with no stated date still needs a place in that order.
    [Fact]
    public void TheEventDateIsTheStatementDateElseTheCaptureDate()
    {
        var captured = new DateOnly(2026, 11, 2);

        Assert.Equal(captured, Revision().EventOn(captured));
        Assert.Equal(Date, (Revision() with { StatementDate = Date, StatementDateEvidence = Span }).EventOn(captured));
    }

    [Fact]
    public void ApprovalNeedsHeadlineSummarySpeakerAndQuotation()
    {
        Revision().ValidateForApproval();

        Assert.Throws<ArgumentException>((Revision() with { Headline = " " }).ValidateForApproval);
        Assert.Throws<ArgumentException>((Revision() with { Summary = "" }).ValidateForApproval);
        Assert.Throws<ArgumentException>((Revision() with { SpeakerOfficialIds = [] }).ValidateForApproval);
        Assert.Throws<ArgumentException>((Revision() with { Quotations = [] }).ValidateForApproval);
    }

    [Fact]
    public void ADateAndItsEvidenceMustComeTogether()
    {
        Assert.Throws<ArgumentException>((Revision() with { StatementDate = Date }).ValidateForApproval);
        Assert.Throws<ArgumentException>((Revision() with { StatementDateEvidence = Span }).ValidateForApproval);
        Assert.Throws<ArgumentException>((Revision() with { StatementDate = Date }).ValidateShape);
        (Revision() with { StatementDate = Date, StatementDateEvidence = Span }).ValidateForApproval();
    }

    [Fact]
    public void AnAiProposedDateNeedsAHumanRevisionBeforeApproval()
    {
        var ai = Revision(ReviewAuthor.AnalysisSubject) with { StatementDate = Date, StatementDateEvidence = Span };

        Assert.Throws<ArgumentException>(ai.RequireConfirmedDate);
        (ai with { RevisionNumber = 2, AuthorSubject = "auth0|owner" }).RequireConfirmedDate();
        (ai with { StatementDate = null, StatementDateEvidence = null }).RequireConfirmedDate();
    }

    [Fact]
    public void ASavedAiDateSurvivesOnlyWhenThePersonTickedThatItWasChecked()
    {
        var ai = Revision(ReviewAuthor.AnalysisSubject) with { StatementDate = Date, StatementDateEvidence = Span };
        var saved = ai with { RevisionNumber = 2, AuthorSubject = "auth0|owner" };

        var dropped = saved.KeepingAiStatementDateOnlyIfChecked(ai, statementDateChecked: false);
        Assert.Null(dropped.StatementDate);
        Assert.Null(dropped.StatementDateEvidence);
        Assert.Equal(Date, saved.KeepingAiStatementDateOnlyIfChecked(ai, statementDateChecked: true).StatementDate);
    }

    [Fact]
    public void ADateThePersonChangedOrAddedIsTheirOwnAndNeedsNoTick()
    {
        var ai = Revision(ReviewAuthor.AnalysisSubject) with { StatementDate = Date, StatementDateEvidence = Span };
        var changed = ai with { RevisionNumber = 2, AuthorSubject = "auth0|owner", StatementDate = Date.AddDays(1) };
        var person = Revision() with { StatementDate = Date, StatementDateEvidence = Span };

        Assert.Equal(Date.AddDays(1), changed.KeepingAiStatementDateOnlyIfChecked(ai, false).StatementDate);
        Assert.Equal(Date, (person with { RevisionNumber = 2 }).KeepingAiStatementDateOnlyIfChecked(person, false).StatementDate);
    }

    [Fact]
    public void ShapeRejectsOversizeTextNulBytesAndBadSpans()
    {
        Revision().ValidateShape();

        Assert.Throws<ArgumentException>((Revision() with { Headline = new string('h', StatementDraftRevision.MaximumHeadlineLength + 1) }).ValidateShape);
        Assert.Throws<ArgumentException>((Revision() with { Summary = "a\0b" }).ValidateShape);
        Assert.Throws<ArgumentException>((Revision() with { OtherSpeakers = [" Jo"] }).ValidateShape);
        Assert.Throws<ArgumentException>((Revision() with { Quotations = [new(Span with { Length = 0 }, StatementQuotationKind.ReleaseText)] }).ValidateShape);
        Assert.Throws<ArgumentException>((Revision() with { Quotations = [.. Revision().Quotations, .. Revision().Quotations] }).ValidateShape);
    }

    [Fact]
    public void SpansMustCiteTheExtractionAndFitInsideItsText()
    {
        var extraction = Extraction();
        var cited = Revision() with { Quotations = [new(new(extraction.ExtractionId, 0, 14), StatementQuotationKind.ReleaseText)] };

        cited.ValidateAgainst(extraction);

        Assert.Throws<ArgumentException>(() => (cited with { Quotations = [new(new(ExtractionId, 0, 14), StatementQuotationKind.ReleaseText)] })
            .ValidateAgainst(extraction));
        Assert.Throws<ArgumentException>(() => (cited with { Quotations = [new(new(extraction.ExtractionId, Text.Length - 3, 14), StatementQuotationKind.ReleaseText)] })
            .ValidateAgainst(extraction));
        Assert.Throws<ArgumentException>(() => (cited with { StatementDate = Date, StatementDateEvidence = new(ExtractionId, 0, 5) })
            .ValidateAgainst(extraction));
    }

    private static DocumentExtraction Extraction()
    {
        var attempt = new CapturedAttemptResult(Guid.NewGuid().ToString("N"), "source", "https://example.test/a", "https://example.test/a",
            DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "text/plain", []), new CaptureIdentity(new string('a', 64), 0));
        return new DocumentExtraction(attempt, "parser", Text);
    }
}
