using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillTexts;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionKeyTermTests
{
    private static readonly string[] GlossaryNames = ["Amendment", "Passage"];
    private static readonly ActionDraftSettings On = new(10_000_000, concurrency: 1, draftsFromBillText: true, keyTermGlossary: GlossaryNames);
    private static readonly ActionDraftSettings Off = new(10_000_000, concurrency: 1, draftsFromBillText: true);

    private static string Plain(string definition) => GpoQuoteMarks.Normalize(definition);

    private static readonly (string Term, string Quote) Secretary = ("Secretary", Plain(SecretaryDefinition));
    private static readonly (string Term, string Quote) Tenant = ("eligible tenant", Plain(TenantDefinition));
    private static readonly (string Term, string Quote) Landlord = ("landlord", Plain(LandlordDefinition));

    [Fact]
    public async Task ADefinitionQuoteBecomesAKeyTermWithPlainQuoteMarksAndAnExactSpanInsideTheDefinitionsSection()
    {
        var (store, model) = await RunAsync(On, [Secretary, Tenant]);

        var revision = Assert.Single(store.Revisions);
        var text = Of(BodyWithDefinitions);
        Assert.Equal(["Secretary", "eligible tenant"], revision.KeyTerms.Select(term => term.Term));
        Assert.Equal([Plain(SecretaryDefinition), Plain(TenantDefinition)], revision.KeyTerms.Select(term => term.Definition));
        Assert.All(revision.KeyTerms, term => Assert.DoesNotContain("``", term.Definition, StringComparison.Ordinal));
        Assert.Equal([SecretaryDefinition, TenantDefinition], revision.KeyTerms.Select(term => text.Text.Substring(term.Span.Start, term.Span.Length)));
        Assert.All(revision.KeyTerms, term => Assert.False(term.Cut));
        var section = BillDefinitionsSection.Find(text.Body)!;
        Assert.All(revision.KeyTerms, term => Assert.True(section.Contains(term.Span.Start - text.BodyStart, term.Span.Length)));
        var request = Assert.Single(model.Requests);
        Assert.Equal(("action-text-prompt-v7", "action-text-schema-v2"), (request.PromptVersion, request.SchemaVersion));
        Assert.Contains("\"keyTerms\"", request.SchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"definitionsSection\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"heading\": \"SEC. 2. DEFINITIONS.\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("SEC. 3. PAYMENTS.", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AQuoteThatIsNotInTheDefinitionsSectionIsSentBackThenDroppedOnTheLastPassSoTheDraftKeepsItsCheckedSummary()
    {
        var outside = (Term: "tenants", Quote: FirstQuote);
        var (store, model) = await RunAsync(On, [outside, Secretary], outputsTwice: true);

        var revision = Assert.Single(store.Revisions);
        Assert.Equal(State.Succeeded, store.Entries[0].Status);
        Assert.Equal($"{FirstPlain} {SecondPlain}", revision.Summary);
        Assert.Equal(["Secretary"], revision.KeyTerms.Select(term => term.Term));
        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("not an exact quote of the definitions section", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        var firstPass = Resolve(await ContextAsync(), [outside]);
        Assert.Contains(firstPass.Errors, error => error.Contains("not an exact quote of the definitions section", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AQuoteThatStartsInTheDefinitionsSectionAndEndsPastTheNextHeadingIsRejected()
    {
        var context = await ContextAsync();
        var across = (Term: "amendment", Quote: "means a change to this Act. SEC. 3. PAYMENTS. The Secretary shall pay");

        var result = Resolve(context, [across]);

        Assert.NotNull(new BillTextQuotes(context.Evidence.BillText!).Find(across.Quote));
        Assert.Contains(result.Errors, error => error.Contains("\"amendment\" is not an exact quote of the definitions section", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AQuoteThatTheTextDoesNotHoldIsRejected()
    {
        var invented = (Term: "Secretary", Quote: "The term \"Secretary\" means the Secretary of Defense.");

        var result = Resolve(await ContextAsync(), [invented]);

        Assert.Contains(result.Errors, error => error.Contains("not an exact quote of the definitions section", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FiveDistinctTermsAreAllStoredAndSixAreRejected()
    {
        var context = await ContextAsync(FiveDefinitionsBody);
        (string Term, string Quote)[] five = [Secretary, Tenant, Landlord, ("rental home", Plain(RentalHomeDefinition)), ("area median", Plain(AreaMedianDefinition))];
        var six = five.Append(("Secretary", Secretary.Quote)).ToArray();

        var refused = Resolve(context, six);
        var accepted = Resolve(context, five);

        Assert.Contains(refused.Errors, error => error.Contains("has 6 terms. Write at most 5.", StringComparison.Ordinal));
        Assert.Empty(accepted.Errors);
        Assert.Equal(five.Select(item => item.Term), accepted.Accepted!.KeyTerms.Select(term => term.Term));
    }

    [Theory]
    [InlineData("Treasurer")]
    [InlineData("S")]
    [InlineData("Housing")]
    [InlineData("secretary")]
    public async Task ATermThatIsNotExactlyTheTermThatTheQuoteMarksInQuotationMarksIsRejected(string term)
    {
        var result = Resolve(await ContextAsync(), [(term, Secretary.Quote)]);

        Assert.Contains(result.Errors, error => error.Contains($"\"{term}\" is not the term that its quote defines in quotation marks", StringComparison.Ordinal));
    }

    [Fact]
    public void ATermEqualsATermThatThePlainDefinitionMarksInQuotationMarks()
    {
        var term = new ActionKeyTerm("eligible tenant", new SourceSpan(ExtractionId, 0, 1), Plain(TenantDefinition), Cut: false);

        Assert.True(term.IsDefinedBy(term.Definition));
        Assert.True(term.IsDefinedBy("The terms \"landlord\" and \"eligible tenant\" mean a party to a lease."));
        Assert.False(term.IsDefinedBy("An eligible tenant is a tenant with low income."));
        Assert.False(term.IsDefinedBy(TenantDefinition));
    }

    [Fact]
    public async Task AQuoteThatEndsBeforeTheEndOfItsDefinitionParagraphIsMarkedCutAndAWholeOneIsNot()
    {
        var context = await ContextAsync();
        var shortened = ("Secretary", "The term \"Secretary\" means the Secretary of Housing");

        var cut = Resolve(context, [shortened, Tenant, ("landlord", "The term \"landlord\" means the owner")]).Accepted!.KeyTerms;

        Assert.Equal([true, false, true], cut.Select(term => term.Cut));
        var section = BillDefinitionsSection.Find(BodyWithDefinitions)!;
        var amendmentEnd = BodyWithDefinitions.IndexOf(AmendmentDefinition, StringComparison.Ordinal) + AmendmentDefinition.Length;
        Assert.False(section.Cuts(BodyWithDefinitions, amendmentEnd));
        Assert.True(section.Cuts(BodyWithDefinitions, amendmentEnd - "to this Act.".Length));
        const string Joined = "SEC. 2. DEFINITIONS. In this Act: (1) Rent.\u2014The term \"rent\" means a payment for a home; and (2) Home.\u2014The term \"home\" means a house.";
        var joined = BillDefinitionsSection.Find(Joined)!;
        Assert.False(joined.Cuts(Joined, Joined.IndexOf("; and", StringComparison.Ordinal)));
        Assert.True(joined.Cuts(Joined, Joined.IndexOf(" for a home", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AShortQuoteAndABlankTermAreRejected()
    {
        var context = await ContextAsync();

        var shortQuote = Resolve(context, [("Secretary", "means the Secretary")]);
        var blank = Resolve(context, [("  ", Secretary.Quote)]);

        Assert.Contains(shortQuote.Errors, error => error.Contains("fewer than 4 words", StringComparison.Ordinal));
        Assert.Contains(blank.Errors, error => error.Contains("A key term has 0 characters", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AGlossaryTermAndARepeatedTermAreDroppedAndTheRestStay()
    {
        var context = await ContextAsync();
        var glossary = ("amendment", Plain(AmendmentDefinition));
        var repeated = ("Secretary", Secretary.Quote);

        var result = Resolve(context, [glossary, Secretary, repeated, Tenant]);

        Assert.Empty(result.Errors);
        Assert.Equal(["Secretary", "eligible tenant"], result.Accepted!.KeyTerms.Select(term => term.Term));
    }

    [Fact]
    public async Task ABillWithNoDefinitionsSectionGetsNoKeyTermsAndItsRequestCarriesNoKeyTermPart()
    {
        var (store, model) = await RunAsync(On, [Secretary], body: Body);

        var revision = Assert.Single(store.Revisions);
        Assert.True(revision.KeyTerms.IsDefaultOrEmpty);
        var request = Assert.Single(model.Requests);
        Assert.DoesNotContain("keyTerms", request.SchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("keyTerms", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("definitionsSection", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithTheSettingOffAnAnswerWithKeyTermsStoresNoneAndTheRequestAsksForNone()
    {
        var (store, model) = await RunAsync(Off, [Secretary]);

        var revision = Assert.Single(store.Revisions);
        Assert.True(revision.KeyTerms.IsDefaultOrEmpty);
        var request = Assert.Single(model.Requests);
        Assert.DoesNotContain("keyTerms", request.SchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("keyTerms", request.SystemPrompt, StringComparison.Ordinal);
        Assert.False(Off.DraftsKeyTerms);
        Assert.True(On.DraftsKeyTerms);
    }

    [Fact]
    public async Task TheApprovalCheckRefusesAKeyTermOutsideTheDefinitionsSectionAndOneWhoseDefinitionIsNotItsSpan()
    {
        var accepted = Resolve(await ContextAsync(), [Secretary]).Accepted!;
        var text = Of(BodyWithDefinitions);
        var extractions = accepted.Citations.Select(span => span.ExtractionId).Distinct().ToDictionary(id => id, _ => Extraction(text));
        var term = accepted.KeyTerms[0];
        var paymentStart = text.Text.IndexOf(FirstQuote, StringComparison.Ordinal);
        var outside = new ActionKeyTerm("tenants", new SourceSpan(ExtractionId, paymentStart, FirstQuote.Length), FirstQuote, Cut: false);

        accepted.ValidateAgainst(extractions);

        Assert.Throws<ArgumentException>(() => (accepted with { KeyTerms = [outside] }).ValidateAgainst(extractions));
        Assert.Throws<ArgumentException>(() => (accepted with { KeyTerms = [term with { Definition = SecretaryDefinition }] }).ValidateAgainst(extractions));
        Assert.Throws<ArgumentException>(() => (accepted with { KeyTerms = [term with { Definition = term.Definition.Replace("Secretary", "Treasurer", StringComparison.Ordinal) }] }).ValidateAgainst(extractions));
        Assert.Throws<ArgumentException>(() => (accepted with { KeyTerms = [term with { Cut = true }] }).ValidateAgainst(extractions));
    }

    [Fact]
    public async Task ARevisionHoldsNoMoreThanFiveKeyTermsAndAPersonEditCannotChangeThem()
    {
        var accepted = Resolve(await ContextAsync(), [Secretary]).Accepted!;
        var term = accepted.KeyTerms[0];

        Assert.Throws<ArgumentException>(() => (accepted with
        {
            KeyTerms = [.. new[] { "one", "two", "three", "four", "five", "six" }.Select(word => term with { Term = word, Definition = $"The term \"{word}\" means it." })]
        }).ValidateShape());
        (accepted with
        {
            KeyTerms = [.. new[] { "one", "two", "three", "four", "five" }.Select(word => term with { Term = word, Definition = $"The term \"{word}\" means it." })]
        }).ValidateShape();
        Assert.Throws<ArgumentException>(() => (accepted with { KeyTerms = [term, term with { Term = "SECRETARY", Definition = "The term \"SECRETARY\" means it." }] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (accepted with { TextPackageId = null, WhyItMatters = null, TextCitations = default }).ValidateShape());
    }

    [Fact]
    public void TheDefinitionsSectionIsTheFirstCapitalLetterDefinitionsHeadingAndEndsAtTheNextHeading()
    {
        var found = BillDefinitionsSection.Find(BodyWithDefinitions)!;

        Assert.Equal("SEC. 2. DEFINITIONS.", found.Heading);
        Assert.Equal("SEC. 3. PAYMENTS.", found.NextHeading);
        Assert.StartsWith("SEC. 2. DEFINITIONS.", BodyWithDefinitions[found.Start..], StringComparison.Ordinal);
        Assert.EndsWith(AmendmentDefinition + " ", BodyWithDefinitions[..found.End], StringComparison.Ordinal);
        Assert.Null(BillDefinitionsSection.Find(Body));
        Assert.Null(BillDefinitionsSection.Find("Sec. 2. Definitions. In this Act: (1) Secretary.\u2014The term ``Secretary'' means the Secretary of Energy."));
        Assert.Null(BillDefinitionsSection.Find("SEC. 2. SHORT TITLE. This Act may be cited as the Rent Aid Act. The term DEFINITIONS means nothing."));
        Assert.Equal("TITLE II\u2014", BillDefinitionsSection.Find("SEC. 9. DEFINITIONS. A term. TITLE II\u2014FUNDS SEC. 10. MONEY.")!.NextHeading);
        Assert.Null(BillDefinitionsSection.Find("SEC. 9. DEFINITIONS. A term here.")!.NextHeading);
    }

    private static AnalysisResolutionOf Resolve(ActionDraftContext context, (string Term, string Quote)[] terms) =>
        new(ActionDraftResolver.ResolveBillText(Output(keyTerms: terms), context, "run", DateTimeOffset.UnixEpoch,
            ActionKeyTermScope.For(context.Evidence.BillText!, new HashSet<string>(GlossaryNames, StringComparer.OrdinalIgnoreCase))));

    private const string RentalHomeDefinition = "The term ``rental home'' means a home that a tenant rents.";
    private const string AreaMedianDefinition = "The term ``area median'' means the median income of the area.";

    private static readonly string FiveDefinitionsBody = "SEC. 2. DEFINITIONS. In this Act: " +
        $"(1) Secretary.\u2014{SecretaryDefinition} (2) Eligible tenant.\u2014{TenantDefinition} (3) Landlord.\u2014{LandlordDefinition} " +
        $"(4) Rental home.\u2014{RentalHomeDefinition} (5) Area median.\u2014{AreaMedianDefinition} SEC. 3. PAYMENTS. {FirstSummary} {SecondSummary} {WhyItMatters}";

    private static async Task<ActionDraftContext> ContextAsync(string? body = null)
    {
        var world = ActionBillWorld.Create(summaries: []);
        var evidence = world.Evidence() with { BillText = Of(body ?? BodyWithDefinitions) };
        var snapshot = (await world.Reader().ReadAsync(evidence, default)).Snapshot!;
        return new ActionDraftContext(evidence, snapshot, BillGateFixtures.HousingChoice(), "housing", BillGateFixtures.Revision().Id);
    }

    private static async Task<(FakeActionDraftStore Store, FakeHeadlineSummaryModel Model)> RunAsync(ActionDraftSettings settings,
        (string Term, string Quote)[] terms, string? body = null, bool outputsTwice = false)
    {
        var world = ActionBillWorld.Create(summaries: []);
        var store = new FakeActionDraftStore(world.Evidence() with { BillText = Of(body ?? BodyWithDefinitions) });
        var model = new FakeHeadlineSummaryModel().Draft(Output(keyTerms: terms));
        if (outputsTwice) model.Draft(Output(keyTerms: terms));
        await AnalysisWorkers.ActionDrafting(store, new FakeAnalysisStatusStore(), world.Reader(), model, settings, BillGateFixtures.Revision(),
            TimeProvider.System).ExecuteAsync(once: true);
        return (store, model);
    }

    private readonly record struct AnalysisResolutionOf(CivicLens.Application.Analysis.AnalysisResolution<ActionDraftRevision> Result)
    {
        public ActionDraftRevision? Accepted => Result.Accepted;
        public IReadOnlyList<string> Errors => Result.Errors;
    }
}
