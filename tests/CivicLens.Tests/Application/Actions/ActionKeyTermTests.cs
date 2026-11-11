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

    private static string Plain(string definition) => BillTextQuotes.Normalize(definition);

    private static readonly (string Term, string Quote) Secretary = ("Secretary", Plain(SecretaryDefinition));
    private static readonly (string Term, string Quote) Tenant = ("eligible tenant", Plain(TenantDefinition));
    private static readonly (string Term, string Quote) Landlord = ("landlord", Plain(LandlordDefinition));

    [Fact]
    public async Task ADefinitionQuoteBecomesAKeyTermWithItsExactStoredCharactersAndItsSpanInsideTheDefinitionsSection()
    {
        var (store, model) = await RunAsync(On, [Secretary, Tenant]);

        var revision = Assert.Single(store.Revisions);
        var text = Of(BodyWithDefinitions);
        Assert.Equal(["Secretary", "eligible tenant"], revision.KeyTerms.Select(term => term.Term));
        Assert.Equal([SecretaryDefinition, TenantDefinition], revision.KeyTerms.Select(term => term.Definition));
        Assert.All(revision.KeyTerms, term => Assert.Equal(term.Definition, text.Text.Substring(term.Span.Start, term.Span.Length)));
        var section = BillDefinitionsSection.Find(text.Body)!;
        Assert.All(revision.KeyTerms, term => Assert.True(section.Contains(term.Span.Start - text.BodyStart, term.Span.Length)));
        var request = Assert.Single(model.Requests);
        Assert.Contains("\"keyTerms\"", request.SchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"definitionsSection\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"heading\": \"SEC. 2. DEFINITIONS.\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("SEC. 3. PAYMENTS.", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AQuoteThatIsNotInTheDefinitionsSectionIsRejectedEvenWhenAnotherSectionHoldsTheWords()
    {
        var outside = (Term: "tenants", Quote: FirstQuote);
        var (store, _) = await RunAsync(On, [outside], outputsTwice: true);

        Assert.Empty(store.Revisions);
        Assert.Equal(State.Failed, store.Entries[0].Status);
        var result = Resolve(await ContextAsync(), [outside]);
        Assert.Contains(result.Errors, error => error.Contains("not an exact quote of the definitions section", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AQuoteThatTheTextDoesNotHoldIsRejected()
    {
        var invented = (Term: "Secretary", Quote: "The term \"Secretary\" means the Secretary of Defense.");

        var result = Resolve(await ContextAsync(), [invented]);

        Assert.Contains(result.Errors, error => error.Contains("not an exact quote of the definitions section", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MoreThanFiveTermsAreRejectedAndFiveAreAccepted()
    {
        var context = await ContextAsync();
        (string Term, string Quote)[] five = [Secretary, Tenant, Landlord, ("amendment", Plain(AmendmentDefinition)), ("Secretary", Secretary.Quote)];
        var six = five.Append(Secretary).ToArray();

        var refused = Resolve(context, six);
        var accepted = Resolve(context, five);

        Assert.Contains(refused.Errors, error => error.Contains("has 6 terms. Write at most 5.", StringComparison.Ordinal));
        Assert.NotNull(accepted.Accepted);
        Assert.Equal(3, accepted.Accepted.KeyTerms.Length);
    }

    [Fact]
    public async Task ATermThatTheQuoteDoesNotHoldIsRejected()
    {
        var result = Resolve(await ContextAsync(), [("Treasurer", Secretary.Quote)]);

        Assert.Contains(result.Errors, error => error.Contains("\"Treasurer\" is not in its quote", StringComparison.Ordinal));
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
        var repeated = ("SECRETARY", Secretary.Quote);

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
        var outside = new ActionKeyTerm("tenants", new SourceSpan(ExtractionId, paymentStart, FirstQuote.Length), FirstQuote);

        accepted.ValidateAgainst(extractions);

        Assert.Throws<ArgumentException>(() => (accepted with { KeyTerms = [outside] }).ValidateAgainst(extractions));
        Assert.Throws<ArgumentException>(() => (accepted with { KeyTerms = [term with { Definition = term.Definition.Replace("Secretary", "Treasurer", StringComparison.Ordinal) }] }).ValidateAgainst(extractions));
    }

    [Fact]
    public async Task ARevisionHoldsNoMoreThanFiveKeyTermsAndAPersonEditCannotChangeThem()
    {
        var accepted = Resolve(await ContextAsync(), [Secretary]).Accepted!;
        var term = accepted.KeyTerms[0];

        Assert.Throws<ArgumentException>(() => (accepted with
        {
            KeyTerms = [.. new[] { "The", "term", "Secretary", "means", "Housing", "Urban" }.Select(word => term with { Term = word })]
        }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (accepted with { KeyTerms = [term, term with { Term = "SECRETARY" }] }).ValidateShape());
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

    private static async Task<ActionDraftContext> ContextAsync()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var evidence = world.Evidence() with { BillText = Of(BodyWithDefinitions) };
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
