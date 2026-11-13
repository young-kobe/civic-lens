using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.ExecutiveActions;

public sealed class ExecutiveSummaryTests
{
    private const string FirstQuote = "I hereby order the Secretary of Commerce to review each import rule within 30 days";
    private const string SecondQuote = "The Secretary shall report the results to the President by March 1, 2025";
    private const string FirstSentence = "The order tells the Secretary of Commerce to review each import rule. The review must finish within 30 days.";
    private const string SecondSentence = "The Secretary must report the results to the President by March 1, 2025.";
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Body = string.Join('\n', [
        "By the authority vested in me as President by the Constitution, " + FirstQuote + ".",
        SecondQuote + ".",
        "Each agency head shall give the Secretary all records that the review needs."]);

    private static DocumentExtraction Extraction(string text, string attempt = "text-attempt") => new(new CapturedAttemptResult(attempt, "source", "https://example.test/a",
        "https://example.test/a", DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "application/json", []),
        new CaptureIdentity(new string('a', 64), 0)), "federal-register-text-v1+html-text-v1", text);

    private static string TextExtractionId => Extraction(FederalRegisterRenderer.RenderText("2025-01900", Body).Text).ExtractionId;

    private static ExecutiveSummaryEvidence Evidence(bool cut = false)
    {
        var document = FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder);
        var factsText = FederalRegisterRenderer.Render(document);
        var facts = new FederalRegisterEvidence(document, Extraction(factsText, "facts-attempt").ExtractionId, factsText, "donald-trump");
        var reading = FederalRegisterRenderer.RenderText(document.DocumentNumber, Body);
        var current = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, Now, facts, []);
        return new(current, new(TextExtractionId, "federal-register-text-v1+html-text-v1", reading.Text, reading.CharsRead,
            cut ? reading.CharsRead + 40_000 : reading.CharsRead), false);
    }

    private static string Output(params (string Sentence, string Quote)[] items) => JsonSerializer.Serialize(new
    {
        summary = new { sentences = items.Select(item => new { sentence = item.Sentence, quote = item.Quote }), confidence = 0.9 }
    });

    private static string Good() => Output((FirstSentence, FirstQuote), (SecondSentence, SecondQuote));

    private static AnalysisResolution<ExecutiveActionRevision> Resolve(string json, ExecutiveSummaryEvidence? evidence = null) =>
        ExecutiveSummaryResolver.Resolve(json, evidence ?? Evidence(), Now);

    [Fact]
    public void EachQuoteBecomesTheSpanOfItsExactCharactersInTheStoredText()
    {
        var evidence = Evidence();

        var resolution = Resolve(Good(), evidence);

        Assert.True(resolution.IsAccepted);
        var revision = resolution.Accepted!;
        Assert.Equal((2, ReviewAuthor.AnalysisSubject), (revision.RevisionNumber, revision.AuthorSubject));
        var quotes = revision.Summary!.Sentences.Select(sentence => evidence.Text.Text.Substring(sentence.Quote.Start, sentence.Quote.Length));
        Assert.Equal([FirstQuote, SecondQuote], quotes);
        Assert.All(revision.Summary.Sentences, sentence => Assert.Equal(TextExtractionId, sentence.Quote.ExtractionId));
        var document = FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder);
        var facts = Extraction(FederalRegisterRenderer.Render(document), "facts-attempt");
        var text = Extraction(evidence.Text.Text);
        revision.ValidateAgainst(new Dictionary<string, DocumentExtraction> { [facts.ExtractionId] = facts, [text.ExtractionId] = text });
    }

    [Theory]
    [InlineData("The Secretary must report the results to the President by April 1, 2025.")]
    [InlineData("I hereby order the Secretary of Commerce to review each import rule.")]
    [InlineData("I hereby order")]
    [InlineData("")]
    public void AQuoteThatIsNotAnExactSpanOfOneLineWithEnoughWordsIsRejectedWithItsReason(string quote)
    {
        var resolution = Resolve(Output((FirstSentence, FirstQuote), (SecondSentence, quote)));

        Assert.False(resolution.IsAccepted);
        Assert.Contains(resolution.Errors, error => error.Contains("quote", StringComparison.Ordinal));
    }

    [Fact]
    public void AQuoteThatCrossesALineBreakHasNoSpan()
    {
        var evidence = Evidence();
        var across = Body.Split('\n')[0][^30..] + "\n" + SecondQuote;

        Assert.Null(ExecutiveSummaryResolver.FindSpan(evidence.Text, across));
        Assert.False(Resolve(Output((FirstSentence, FirstQuote), (SecondSentence, across)), evidence).IsAccepted);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AnySummaryOfOtherThanTwoOrThreeSentencesIsRejected(int count)
    {
        var items = Enumerable.Range(0, count).Select(_ => (FirstSentence, FirstQuote)).ToArray();

        var resolution = Resolve(Output(items));

        Assert.False(resolution.IsAccepted);
        Assert.Contains(resolution.Errors, error => error.StartsWith($"The summary has {count} sentences", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("This order aims to cut costs for the Secretary of Commerce.", "aims to")]
    [InlineData("The Secretary Seeks To review each import rule within 30 days.", "seeks to")]
    [InlineData("The review is intended to finish within 30 days.", "intended to")]
    public void ASentenceWithAPhraseOfTheClosedListIsRejectedAndTheReasonNamesThePhrase(string sentence, string phrase)
    {
        var resolution = Resolve(Output((sentence, FirstQuote), (SecondSentence, SecondQuote)));

        Assert.False(resolution.IsAccepted);
        Assert.Contains(resolution.Errors, error => ExecutiveSummaryPhrases.IsReason(error) && error.Contains($"\"{phrase}\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("The Secretary claims to review each import rule.")]
    [InlineData("The order names an aim to review each import rule.")]
    public void AWordThatOnlyContainsAPhraseIsNotAPhrase(string sentence) =>
        Assert.Null(ExecutiveSummaryPhrases.FindIn(sentence));

    [Fact]
    public void ASummaryThatReadsAboveGradeTwelveAsOnePassageIsRejected()
    {
        const string Hard = "Notwithstanding antecedent administrative determinations, interdepartmental coordination necessitates comprehensive reconsideration of multifaceted regulatory methodologies. " +
            "Implementation responsibilities notwithstanding, extraordinarily consequential institutional accountability mechanisms require comprehensive documentation.";

        var resolution = Resolve(Output((Hard, FirstQuote), (Hard, SecondQuote)));

        Assert.False(resolution.IsAccepted);
        Assert.Contains(resolution.Errors, error => error.Contains("as one passage", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCutFlagIsTrueExactlyWhenTheStoredTextKeepsLessThanTheDocument(bool cut)
    {
        var evidence = Evidence(cut);

        var revision = Resolve(Good(), evidence).Accepted!;

        Assert.Equal(cut, revision.Summary!.Cut);
        Assert.Equal(cut, evidence.Text.CharsRead < evidence.Text.CharsTotal);
        Assert.Contains($"\"cut\":{(cut ? "true" : "false")}", ExecutiveSummaryPrompt.Build(evidence, 100_000)!.UserPrompt.Replace(" ", "", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePromptSendsTheBodyOfTheTextAndNotItsHeaderLines()
    {
        var evidence = Evidence();

        var request = ExecutiveSummaryPrompt.Build(evidence, 100_000)!;

        Assert.Contains(FirstQuote, request.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Characters read:", request.UserPrompt, StringComparison.Ordinal);
        Assert.Equal((ExecutiveSummaryTask.PromptVersion, ExecutiveSummaryTask.SchemaVersion), (request.PromptVersion, request.SchemaVersion));
        Assert.Null(ExecutiveSummaryPrompt.Build(evidence, 100));
    }

    [Fact]
    public async Task TheRepairPassSendsBackThePhraseThatTheFirstAnswerUsedAndTheMeasureCountsIt()
    {
        var model = new FakeExecutiveSummaryModel()
            .Draft(Output(("This order aims to speed up the review of each import rule.", FirstQuote), (SecondSentence, SecondQuote)))
            .Draft(Good());

        var report = await MeasureAsync(model);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Accepted, 2, 1, false), (bill.Outcome, bill.Calls, bill.PhraseRejections, bill.Cut));
        Assert.DoesNotContain("uses the phrase", model.Requests[0].UserPrompt, StringComparison.Ordinal);
        Assert.Contains("uses the phrase \"aims to\"", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([FirstQuote, SecondQuote], bill.Sentences.Select(sentence => sentence.Quote));
    }

    [Fact]
    public async Task OneAnswerWithAPhraseInTwoSentencesCountsAsOnePhraseRejection()
    {
        var model = new FakeExecutiveSummaryModel()
            .Draft(Output(("This order aims to speed up the review of each import rule.", FirstQuote), ("The Secretary seeks to report the results to the President by March 1, 2025.", SecondQuote)))
            .Draft(Good());

        var report = await MeasureAsync(model);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Accepted, 2, 1), (bill.Outcome, bill.Calls, bill.PhraseRejections));
        Assert.Equal(2, bill.Rejections.Count(ExecutiveSummaryPhrases.IsReason));
    }

    [Fact]
    public async Task AnAnswerWithAPhraseAfterTheRepairPassIsRejectedAndNoSummaryIsAccepted()
    {
        var bad = Output(("This order seeks to speed up the review of each import rule.", FirstQuote), (SecondSentence, SecondQuote));
        var model = new FakeExecutiveSummaryModel().Draft(bad).Draft(bad);

        var report = await MeasureAsync(model);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Rejected, 2, 2), (bill.Outcome, bill.Calls, bill.PhraseRejections));
        Assert.Empty(bill.Sentences);
    }

    [Fact]
    public async Task TheMeasureReportsTheCutFlagAndTheCountsOfACutText()
    {
        var report = await MeasureAsync(new FakeExecutiveSummaryModel().Draft(Good()), Evidence(cut: true));

        var bill = Assert.Single(report.Bills);
        Assert.True(bill.Cut);
        Assert.Equal((bill.CharsRead + 40_000, ExecutiveSummaryTask.PromptVersion), (bill.CharsTotal, report.PromptVersion));
    }

    [Fact]
    public async Task TheMeasureSendsNoCallForADocumentWithoutARecordOrText()
    {
        var model = new FakeExecutiveSummaryModel();
        var measure = new ExecutiveSummaryMeasure(new FakeExecutiveSummaryStore(null), Step(model));

        var report = await measure.RunAsync(["2025-01900"], 1_000_000, default);

        Assert.Equal(ActionTextMeasureReason.NoRecord, Assert.Single(report.Bills).Reason);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void ACorrectionAfterTheSummaryKeepsItAndHasTheCodeAuthorSoTheRunBelongsToTheSummaryRevisionOnly()
    {
        var summary = Resolve(Good()).Accepted!;
        var notice = new ExecutiveActionNotice("R1-2025-01900", new DateOnly(2025, 2, 1), "https://example.test/n", summary.Facts[0].Citation);

        var correction = ExecutiveActionRevisions.Add(summary, Now.AddDays(1), [notice]);

        correction.ValidateShape();
        Assert.Equal((3, ReviewAuthor.CodeSubject), (correction.RevisionNumber, correction.AuthorSubject));
        Assert.Equal(summary.Summary, correction.Summary);
    }

    [Fact]
    public void ARevisionOfTheAnalysisAuthorWithNoSummaryIsRefusedAndSoIsASummaryOfOnePersonOrTwoExtractions()
    {
        var summary = Resolve(Good()).Accepted!;
        var other = new string('9', 64);

        Assert.Throws<ArgumentException>((summary with { Summary = null }).ValidateShape);
        Assert.Throws<ArgumentException>((summary with { AuthorSubject = "auth0|someone" }).ValidateShape);
        Assert.Throws<ArgumentException>((summary with
        {
            Summary = summary.Summary! with { Sentences = [summary.Summary.Sentences[0], summary.Summary.Sentences[1] with { Quote = summary.Summary.Sentences[1].Quote with { ExtractionId = other } }] }
        }).ValidateShape);
    }

    [Fact]
    public void AQuoteInTheHeaderLinesOfTheTextIsNotInTheBodyAndFailsTheApprovalCheck()
    {
        var evidence = Evidence();
        var summary = Resolve(Good(), evidence).Accepted!;
        var headerLine = evidence.Text.Text.Split('\n')[1];
        Assert.True(headerLine.StartsWith("Characters read:", StringComparison.Ordinal) && headerLine.Split(' ').Length >= 4);
        var headerQuote = summary.Summary!.Sentences[1].Quote with { Start = evidence.Text.Text.IndexOf(headerLine, StringComparison.Ordinal), Length = headerLine.Length };
        var header = summary with { Summary = summary.Summary with { Sentences = [summary.Summary.Sentences[0], summary.Summary.Sentences[1] with { Quote = headerQuote }] } };
        var document = FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder);
        var extractions = new Dictionary<string, DocumentExtraction>
        {
            [Extraction("x", "facts-attempt").ExtractionId] = Extraction(FederalRegisterRenderer.Render(document), "facts-attempt"),
            [evidence.Text.ExtractionId] = Extraction(evidence.Text.Text)
        };

        Assert.Throws<ArgumentException>(() => ExecutiveSummaryApproval.Validate(header, extractions));
        ExecutiveSummaryApproval.Validate(summary, extractions);
    }

    [Fact]
    public void ASummaryThatReadsAboveGradeTwelveFailsTheApprovalCheckEvenWhenItsQuotesAreExact()
    {
        const string Hard = "Notwithstanding antecedent administrative determinations, interdepartmental coordination necessitates comprehensive reconsideration of multifaceted regulatory methodologies.";
        var evidence = Evidence();
        var summary = Resolve(Good(), evidence).Accepted!;
        var hard = summary with { Summary = summary.Summary! with { Sentences = [.. summary.Summary.Sentences.Select(sentence => sentence with { Sentence = Hard })] } };
        var document = FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder);
        var extractions = new Dictionary<string, DocumentExtraction>
        {
            [Extraction("x", "facts-attempt").ExtractionId] = Extraction(FederalRegisterRenderer.Render(document), "facts-attempt"),
            [evidence.Text.ExtractionId] = Extraction(evidence.Text.Text)
        };

        var failure = Assert.Throws<ArgumentException>(() => ExecutiveSummaryApproval.Validate(hard, extractions));

        Assert.Contains("as one passage", failure.Message, StringComparison.Ordinal);
    }

    private static ExecutiveSummaryStep Step(FakeExecutiveSummaryModel model) =>
        new(new FakeExecutiveSummaryStore(Evidence()), model, new ExecutiveSummarySettings(10_000_000, 1));

    private static Task<ActionTextMeasureReport> MeasureAsync(FakeExecutiveSummaryModel model, ExecutiveSummaryEvidence? evidence = null) =>
        new ExecutiveSummaryMeasure(new FakeExecutiveSummaryStore(evidence ?? Evidence()), Step(model)).RunAsync(["2025-01900"], 10_000_000, default);
}
