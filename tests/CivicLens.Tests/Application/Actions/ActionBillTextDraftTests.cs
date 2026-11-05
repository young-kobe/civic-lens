using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillTexts;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionBillTextDraftTests
{
    private static readonly SummarySpec Summary = new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>");
    private static readonly ActionDraftSettings On = new(10_000_000, concurrency: 1, draftsFromBillText: true);

    [Fact]
    public async Task ABillWithNoSummaryDraftsFromItsTextAndEverySentenceCitesItsQuoteOfThatText()
    {
        var (store, model) = await RunAsync(ActionBillWorld.Create(summaries: []), new FakeHeadlineSummaryModel().Draft(Output()), On);

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((PackageId, ReviewTextOrigin.Model, null), (revision.TextPackageId, revision.TextOrigin, revision.SummaryCitation));
        Assert.Equal($"{FirstPlain} {SecondPlain}", revision.Summary);
        Assert.Equal(WhyPlain, revision.WhyItMatters);
        var text = Of();
        Assert.Equal([FirstQuote, SecondQuote, WhyQuote],
            revision.TextCitations.Select(citation => text.Text.Substring(citation.Span.Start, citation.Span.Length)));
        Assert.Equal([FirstPlain, SecondPlain, WhyPlain], revision.TextCitations.Select(citation => citation.Sentence));
        Assert.Equal([ActionTextPart.Summary, ActionTextPart.Summary, ActionTextPart.WhyItMatters], revision.TextCitations.Select(citation => citation.Part));
        Assert.All(revision.TextCitations, citation => Assert.Equal(ExtractionId, citation.Span.ExtractionId));
        var run = Assert.Single(model.Requests);
        Assert.Equal((ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion), (run.PromptVersion, run.SchemaVersion));
        Assert.Equal((State.Succeeded, null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ARevisionWhoseSentencesContainTheirQuotesPassesTheApprovalCheckAndTextThatBreaksThatRuleFailsIt()
    {
        var (store, _) = await RunAsync(ActionBillWorld.Create(summaries: []), new FakeHeadlineSummaryModel().Draft(Output()), On);
        var revision = Assert.Single(store.Revisions);
        var extractions = revision.Citations.Select(span => span.ExtractionId).Distinct().ToDictionary(id => id, _ => Extraction(Of()));
        ActionDraftRevision WithSentence(string sentence) => revision with
        {
            Summary = $"{sentence} {SecondPlain}",
            TextCitations = revision.TextCitations.SetItem(0, revision.TextCitations[0] with { Sentence = sentence })
        };

        revision.ValidateAgainst(extractions);

        Assert.Throws<ArgumentException>(() => (revision with { Summary = "Rent aid is paid to landlords." }).ValidateAgainst(extractions));
        Assert.Throws<ArgumentException>(() => (revision with { WhyItMatters = "The program is bad." }).ValidateAgainst(extractions));
        Assert.Throws<ArgumentException>(() => WithSentence("The bill pays landlords for tenants.").ValidateAgainst(extractions));
        WithSentence("Landlords are paid: pay landlords on behalf of tenants with low income.").ValidateAgainst(extractions);
    }

    [Fact]
    public async Task ATextCitationMustLieInTheBodyOfOneExtractionSoAHeaderSpanOrASpanOfAnotherExtractionFailsTheCheck()
    {
        var (store, _) = await RunAsync(ActionBillWorld.Create(summaries: []), new FakeHeadlineSummaryModel().Draft(Output()), On);
        var revision = Assert.Single(store.Revisions);
        var other = new string('f', 64);
        var extractions = revision.Citations.Select(span => span.ExtractionId).Append(other).Distinct().ToDictionary(id => id, _ => Extraction(Of()));
        ActionDraftRevision With(SourceSpan span) => revision with { TextCitations = revision.TextCitations.SetItem(0, revision.TextCitations[0] with { Span = span }) };

        revision.ValidateAgainst(extractions);

        var header = Assert.Throws<ArgumentException>(() => With(new(ExtractionId, 0, 20)).ValidateAgainst(extractions));
        var foreign = Assert.Throws<ArgumentException>(() => With(revision.TextCitations[0].Span with { ExtractionId = other }).ValidateAgainst(extractions));
        Assert.Contains("body", header.Message, StringComparison.Ordinal);
        Assert.Contains("body", foreign.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheApprovalCheckRefusesAQuoteOfThreeWordsAndAcceptsOneOfFour()
    {
        var context = await ContextAsync();
        var fourWords = "This Act may be";
        var drafted = ActionDraftResolver.ResolveBillText(Output([(FirstPlain, FirstQuote), (SecondPlain, SecondQuote)], [($"It starts: {fourWords}.", fourWords)]),
            context, "run", DateTimeOffset.UnixEpoch).Accepted!;
        var last = drafted.TextCitations[^1];
        var threeWords = drafted with
        {
            WhyItMatters = "It starts: This Act may.",
            TextCitations = drafted.TextCitations.SetItem(drafted.TextCitations.Length - 1,
                last with { Span = last.Span with { Length = "This Act may".Length }, Sentence = "It starts: This Act may." })
        };

        var extractions = drafted.Citations.Select(span => span.ExtractionId).Distinct().ToDictionary(id => id, _ => Extraction(Of()));

        drafted.ValidateAgainst(extractions);

        Assert.Throws<ArgumentException>(() => threeWords.ValidateAgainst(extractions));
    }

    [Fact]
    public async Task TheResolverRefusesAQuoteThatIsNotInTheTextSoNoInventedQuoteReachesAReader()
    {
        var context = await ContextAsync();
        var invented = Output([(FirstPlain, FirstQuote), ("The bill will pay every landlord in the country.", "pay every landlord in the country")]);

        var refused = ActionDraftResolver.ResolveBillText(invented, context, "run", DateTimeOffset.UnixEpoch);
        var accepted = ActionDraftResolver.ResolveBillText(Output(), context, "run", DateTimeOffset.UnixEpoch);

        Assert.False(refused.IsAccepted);
        Assert.Contains(refused.Errors, error => error.Contains("not an exact quote of the bill text", StringComparison.Ordinal));
        Assert.True(accepted.IsAccepted);
    }

    [Fact]
    public async Task TheResolverRefusesASentenceThatDoesNotContainItsQuote()
    {
        var context = await ContextAsync();
        var apart = Output([(FirstPlain, FirstQuote), ("A tenant can get help from the bill.", SecondQuote)]);

        var refused = ActionDraftResolver.ResolveBillText(apart, context, "run", DateTimeOffset.UnixEpoch);

        Assert.False(refused.IsAccepted);
        Assert.Contains(refused.Errors, error => error.Contains("does not contain its quote", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("This Act may", false)]
    [InlineData("This Act may be", true)]
    public async Task TheResolverRefusesAQuoteOfThreeWordsAndAcceptsOneOfFour(string quote, bool accepted)
    {
        var output = Output([(FirstPlain, FirstQuote), ($"The bill says {quote}.", quote)]);

        var result = ActionDraftResolver.ResolveBillText(output, await ContextAsync(), "run", DateTimeOffset.UnixEpoch);

        Assert.Equal(accepted, result.IsAccepted);
        if (!accepted) Assert.Contains(result.Errors, error => error.Contains("fewer than 4 words", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheResolverRefusesAQuoteThatLiesPastTheCharactersTheModelRead()
    {
        var text = Of();
        var cut = text with { CharsRead = text.Body.IndexOf(SecondQuote, StringComparison.Ordinal) };
        var whole = await ContextAsync();
        var context = whole with { Evidence = whole.Evidence with { BillText = cut } };

        var result = ActionDraftResolver.ResolveBillText(Output(), context, "run", DateTimeOffset.UnixEpoch);

        Assert.False(result.IsAccepted);
        Assert.Contains(result.Errors, error => error.Contains(SecondQuote[..20], StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 4)]
    public async Task TheResolverRefusesTooFewOrTooManySentencesInEitherPart(int summaryCount, int whyCount)
    {
        var summary = new[] { (FirstPlain, FirstQuote), (SecondPlain, SecondQuote), (WhyPlain, WhyQuote), (FirstPlain, FirstQuote) }.Take(summaryCount).ToArray();
        var why = new[] { (WhyPlain, WhyQuote), (FirstPlain, FirstQuote), (SecondPlain, SecondQuote), (FirstPlain, FirstQuote) }.Take(whyCount).ToArray();

        var result = ActionDraftResolver.ResolveBillText(Output(summary, why), await ContextAsync(), "run", DateTimeOffset.UnixEpoch);

        Assert.False(result.IsAccepted);
    }

    [Fact]
    public async Task ASummaryWinsOverTheTextAndTheTextWinsOverTheCodeWrite()
    {
        var withSummary = ActionBillWorld.Create(summaries: [Summary]);
        var (summaryStore, summaryModel) = await RunAsync(withSummary, new FakeHeadlineSummaryModel().Draft(
            HeadlineSummaryOutputs.Output(summary: "Elizabeth Warren introduced a housing bill.", issueIds: ["rent"])), On);
        var (textStore, textModel) = await RunAsync(ActionBillWorld.Create(summaries: []), new FakeHeadlineSummaryModel().Draft(Output()), On);
        var (codeStore, codeModel) = await RunAsync(ActionBillWorld.Create(summaries: [], policyArea: BillGateFixtures.UntrackedPolicyArea),
            new FakeHeadlineSummaryModel(), On, withText: false);

        var fromSummary = Assert.Single(summaryStore.Revisions);
        Assert.Equal((null, "00@2025-01-05T10:00:00Z"), (fromSummary.TextPackageId, fromSummary.SummaryVersion));
        Assert.Null(Assert.Single(summaryModel.Requests).PromptVersion);
        Assert.Equal(PackageId, Assert.Single(textStore.Revisions).TextPackageId);
        Assert.Equal(ActionDraftTask.TextPromptVersion, Assert.Single(textModel.Requests).PromptVersion);
        Assert.Equal((ReviewTextOrigin.Code, null), (Assert.Single(codeStore.Revisions).TextOrigin, Assert.Single(codeStore.Revisions).TextPackageId));
        Assert.Empty(codeModel.Requests);
    }

    [Fact]
    public async Task WithTheSettingOffABillWithNoSummaryStillGetsTheCodeWrite()
    {
        var off = new ActionDraftSettings(10_000_000, concurrency: 1);

        var (store, model) = await RunAsync(ActionBillWorld.Create(summaries: [], policyArea: BillGateFixtures.UntrackedPolicyArea),
            new FakeHeadlineSummaryModel(), off);

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((ReviewTextOrigin.Code, null, null), (revision.TextOrigin, revision.TextPackageId, revision.WhyItMatters));
        Assert.Equal(ActionDraftErrorCodes.CodeWritten, Assert.Single(store.Runs).Reason);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ThePromptSendsOnlyTheFirstSixtyThousandCharactersOfALongTextAndStatesTheTotal()
    {
        var long_ = string.Concat(Enumerable.Repeat("word ", 14_000)) + "TAIL-AFTER-THE-WINDOW";
        var text = Of(long_);
        var world = ActionBillWorld.Create(summaries: []);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence() with { BillText = text }, default)).Snapshot!;

        var user = ActionTextPrompt.Build(snapshot, text, BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!.UserPrompt;

        using var facts = JsonDocument.Parse(user["Facts:\n".Length..]);
        var sent = facts.RootElement.GetProperty("billText");
        var body = sent.GetProperty("text").GetString()!;
        Assert.True(body.Length <= CongressBillRenderer.TextWindowLength);
        Assert.Equal(text.CharsRead, body.Length);
        Assert.StartsWith("word word", body, StringComparison.Ordinal);
        Assert.Equal((text.CharsRead, long_.Length), (sent.GetProperty("charactersRead").GetInt32(), sent.GetProperty("charactersTotal").GetInt32()));
        Assert.DoesNotContain("TAIL-AFTER-THE-WINDOW", user, StringComparison.Ordinal);
        Assert.DoesNotContain("Characters read", user, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATextOfAnOlderExtractionVersionOrWithAWindowThatDisagreesWithItsCountsBlocksTheDraftBeforeAnyCall()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var old = await RunAsync(world, new FakeHeadlineSummaryModel(), On, shape: evidence => evidence with { BillText = Of(extractionVersion: "congress-bill-text-v0") });
        var cut = await RunAsync(world, new FakeHeadlineSummaryModel(), On, shape: evidence => evidence with { BillText = Of() with { CharsRead = 7 } });

        Assert.Equal((State.Blocked, ActionDraftErrorCodes.ExtractionVersionChanged), (old.Store.Entries[0].Status, old.Store.Entries[0].ErrorCode));
        Assert.Equal((State.Blocked, ActionDraftErrorCodes.RenderedTextMismatch), (cut.Store.Entries[0].Status, cut.Store.Entries[0].ErrorCode));
        Assert.Empty(old.Model.Requests);
        Assert.Empty(cut.Model.Requests);
    }

    [Fact]
    public async Task ASecondRunOnTheSameTextIsUnchangedAndAChangedPackageDraftsAgain()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var (first, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()), On);
        var drafted = Assert.Single(first.Revisions);

        var (_, same) = await RunAsync(world, new FakeHeadlineSummaryModel(), On, current: drafted);
        var (second, other) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()), On, current: drafted with { TextPackageId = "BILLS-119hr1rh" });

        Assert.Empty(same.Requests);
        Assert.Single(other.Requests);
        Assert.Equal(PackageId, Assert.Single(second.Revisions).TextPackageId);
    }

    [Fact]
    public async Task TurningTheSettingOffReplacesNoTextDraftBecauseThePackageIsComparedOnlyWhileTheSettingIsOn()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var (first, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()), On);
        var drafted = Assert.Single(first.Revisions);
        var off = new ActionDraftSettings(10_000_000, concurrency: 1);

        var (second, model) = await RunAsync(world, new FakeHeadlineSummaryModel(), off, current: drafted, withText: false);

        Assert.Empty(second.Revisions);
        Assert.Empty(model.Requests);
        Assert.Equal((State.Succeeded, ActionDraftErrorCodes.Unchanged), (second.Entries[0].Status, second.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ATextPromptOverTheByteCapFallsBackToTheCodeWriteLikeTheSummaryPathInsteadOfBlockingTheEntry()
    {
        var tight = new ActionDraftSettings(ActionDraftPrompt.MinimumReservation, concurrency: 1, draftsFromBillText: true);

        var (store, model) = await RunAsync(ActionBillWorld.Create(summaries: [], policyArea: BillGateFixtures.UntrackedPolicyArea),
            new FakeHeadlineSummaryModel(), tight);

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((ReviewTextOrigin.Code, null), (revision.TextOrigin, revision.TextPackageId));
        Assert.Equal((State.Succeeded, null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    private static async Task<ActionDraftContext> ContextAsync()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var evidence = world.Evidence() with { BillText = Of() };
        var snapshot = (await world.Reader().ReadAsync(evidence, default)).Snapshot!;
        return new ActionDraftContext(evidence, snapshot, BillGateFixtures.HousingChoice(), "housing", BillGateFixtures.Revision().Id);
    }

    private static async Task<(FakeActionDraftStore Store, FakeHeadlineSummaryModel Model)> RunAsync(ActionBillWorld world, FakeHeadlineSummaryModel model,
        ActionDraftSettings settings, ActionDraftRevision? current = null, bool withText = true,
        Func<ActionDraftEvidence, ActionDraftEvidence>? shape = null)
    {
        var evidence = world.Evidence(current) with { BillText = withText ? Of() : null };
        var store = new FakeActionDraftStore(shape is null ? evidence : shape(evidence));
        await AnalysisWorkers.ActionDrafting(store, new FakeAnalysisStatusStore(), world.Reader(), model, settings, BillGateFixtures.Revision(),
            TimeProvider.System).ExecuteAsync(once: true);
        return (store, model);
    }
}
