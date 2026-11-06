using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Application.Votes;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.VoteWorld;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteDecidesStepTests
{
    private const string FirstSentence = "This bill gives $50 million to the Department of Housing for rent aid in 2026.";
    private const string SummaryHtml = FirstSentence + " It starts on January 1, 2026.";
    private static readonly string SummaryText = ActionBillWorld.FragmentExtractor.Text(SummaryHtml);
    private const string Headline = "The House voted on a concurrent resolution";
    private const string HeadlineSummary = "The House decided whether to agree to a Senate amendment.";
    private const string Decides = "The bill gives $50 million to the Department of Housing for rent aid in 2026. The question passed.";
    private const string Quote = "gives $50 million to the Department of Housing for rent aid in 2026";

    private static string Answer(string? decides = Decides, string quote = Quote) =>
        HeadlineSummaryOutputs.Output(Headline, HeadlineSummary, issueIds: ["rent"], decides: decides is null ? null : (decides, quote));

    private static VoteWorld World(string? question = null, string? summaryHtml = SummaryHtml) => Create(question: question, summaryHtml: summaryHtml);

    [Fact]
    public async Task AContentVoteGetsTheSummaryTheBillLineAndThePlainWordsAndStoresTheCheckedTextWithItsSourceHash()
    {
        var (store, model) = await RunAsync(World(), new FakeHeadlineSummaryModel().Draft(Answer()));

        var request = Assert.Single(model.Requests);
        Assert.Contains("\"summary\": \"" + SummaryText + "\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"billLine\": \"Text of " + FirstSentence + "\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"question\": \"Vote to pass or agree to (On Motion to Concur in the Senate Amendment)\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"result\": \"Passed\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"decides\":{\"type\":\"object\"", request.SchemaJson, StringComparison.Ordinal);
        var decides = Assert.Single(store.Revisions).Decides!;
        Assert.Equal((Decides, Quote, ReviewTextOrigin.Model, VoteDecides.Label), (decides.Text, decides.Quote, decides.TextOrigin, VoteDecides.Label));
        Assert.Matches("^[0-9a-f]{64}$", decides.SourceHash);
    }

    [Fact]
    public async Task AProcedureVoteGetsTheBillLineAloneAndNoSummaryBecauseItDecidesAStepAndNotWhatTheBillDoes()
    {
        var answer = Answer("The vote decides whether to send the bill back to committee.", "gives $50 million to the Department of Housing");

        var (store, model) = await RunAsync(World("On Motion to Recommit"), new FakeHeadlineSummaryModel().Draft(answer));

        var request = Assert.Single(model.Requests);
        Assert.DoesNotContain("It starts on January 1", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"billLine\": \"Text of " + FirstSentence + "\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("This question decides a step in the process", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("This question decides what the bill does", request.SystemPrompt, StringComparison.Ordinal);
        Assert.NotNull(Assert.Single(store.Revisions).Decides);
    }

    [Fact]
    public async Task AVoteOnABillWithNoSummaryAsksNoDecidesTextBecauseNothingBacksAQuote()
    {
        var (store, model) = await RunAsync(World(summaryHtml: null), new FakeHeadlineSummaryModel().Draft(Answer(decides: null)));

        var request = Assert.Single(model.Requests);
        Assert.DoesNotContain("\"decides\"", request.UserPrompt + request.SchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("The field decides", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Null(Assert.Single(store.Revisions).Decides);
    }

    [Fact]
    public async Task TheNewestSummaryByActionDateIsTheOneTheModelReadWhateverTheOrderOfThePages()
    {
        var world = Create(summaryHtml: SummaryHtml, olderSummaryHtml: "This older text gives $10 million to old programs in 2020.");

        var (_, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Answer()));

        var prompt = Assert.Single(model.Requests).UserPrompt;
        Assert.Contains(FirstSentence, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("older text", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASummaryThatGivesNoBillLineAsksNoDecidesTextBecauseTheBillLineOwnerFoundNoSentence()
    {
        var (store, model) = await RunAsync(World(summaryHtml: "A concurrent resolution on rent"), new FakeHeadlineSummaryModel().Draft(Answer(decides: null)));

        Assert.DoesNotContain("\"decides\"", Assert.Single(model.Requests).UserPrompt, StringComparison.Ordinal);
        Assert.Null(Assert.Single(store.Revisions).Decides);
    }

    [Fact]
    public async Task ADecidesFieldOnAVoteThatAskedForNoneIsRejected()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Answer()).Draft(Answer(decides: null));

        var (store, _) = await RunAsync(World(summaryHtml: null), model);

        Assert.Contains("takes no decides field", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Null(Assert.Single(store.Revisions).Decides);
    }

    [Theory]
    [InlineData("The bill gives $50 million to rent aid.", "gives $50 million to the Department of Housing for rent aid in 2026 and more", "not an exact part of the summary")]
    [InlineData("The bill gives $50 million to rent aid.", "gives $50 million", "at least 4 words")]
    [InlineData("The bill gives $60 million to rent aid.", Quote, "states $60 million")]
    [InlineData("The bill gives $50 million to rent aid in 2031.", Quote, "states 2031")]
    [InlineData("The bill gives $50 million to rent aid. The vote was 3 to 1.", Quote, "the number 3, which is a vote count")]
    [InlineData("Robert Aderholt wrote this bill that gives $50 million.", Quote, "so name no person")]
    [InlineData("The bill gives $50 million to rent aid. The question failed.", Quote, "The result is \"Passed\"")]
    [InlineData("The bill gives $50 million to rent aid. The question was not confirmed.", Quote, "The decides text says \"not confirmed\"")]
    [InlineData("Notwithstanding the aforementioned appropriations, the Secretary of Housing and Urban Development shall administer discretionary expenditures notwithstanding statutory limitations.", Quote, "reads at grade")]
    public async Task AFailedCheckGoesBackToTheModelWithItsReasonAndAFixedAnswerIsAccepted(string badText, string badQuote, string reason)
    {
        var model = new FakeHeadlineSummaryModel().Draft(Answer(badText, badQuote)).Draft(Answer());

        var (store, _) = await RunAsync(World(), model);

        Assert.Contains(reason, model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
        Assert.Equal(Decides, Assert.Single(store.Revisions).Decides!.Text);
    }

    [Fact]
    public async Task AnAnswerThatKeepsFailingTheChecksWritesNoRevisionAndNoText()
    {
        var bad = Answer("The bill gives $60 million to rent aid.");

        var (store, model) = await RunAsync(World(), new FakeHeadlineSummaryModel().Draft(bad).Draft(bad));

        Assert.Equal(2, model.Requests.Count);
        Assert.Empty(store.Revisions);
        Assert.Equal((State.Failed, "outputRejected"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task AResultWordIsRefusedWhenTheCodeResultHasNoPlainWordBecauseNoWordCouldEqualIt()
    {
        var unknown = Create(result: "Vote Postponed", summaryHtml: SummaryHtml);
        var model = new FakeHeadlineSummaryModel().Draft(Answer()).Draft(Answer("The bill gives $50 million to rent aid."));

        var (store, _) = await RunAsync(unknown, model);

        Assert.Contains("Write no result word, because the result is \"Vote Postponed\"", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal("The bill gives $50 million to rent aid.", Assert.Single(store.Revisions).Decides!.Text);
    }

    [Fact]
    public async Task AChangedSummaryDraftsAgainBecauseTheSourceHashNamesWhatTheModelRead()
    {
        var current = await FirstRevisionAsync(World());
        var model = new FakeHeadlineSummaryModel().Draft(Answer());

        var (store, _) = await RunAsync(World(summaryHtml: SummaryHtml + " It ends in 2027."), model, current);

        Assert.Single(model.Requests);
        Assert.NotEqual(current.Decides!.SourceHash, Assert.Single(store.Revisions).Decides!.SourceHash);
    }

    [Fact]
    public async Task AVoteThatChangedFromProcedureToContentDraftsAgainBecauseTheSourceHashHoldsTheMode()
    {
        var current = await FirstRevisionAsync(World("On Motion to Recommit"), Answer("The vote decides whether to send the bill back.", "gives $50 million to the Department of Housing"));
        var model = new FakeHeadlineSummaryModel().Draft(Answer());

        var (store, _) = await RunAsync(World(), model, current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Single(model.Requests);
        Assert.Equal(Decides, Assert.Single(store.Revisions).Decides!.Text);
    }

    [Fact]
    public async Task AChangeOfMemberVotesAloneKeepsTheDecidesTextWithoutAModelCall()
    {
        var current = await FirstRevisionAsync(World());
        var changed = Create(members: [new(Aderholt, "Robert", "Aderholt", "Nay")], summaryHtml: SummaryHtml);
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(changed, model, current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        Assert.Equal(current.Decides, Assert.Single(store.Revisions).Decides);
    }

    [Fact]
    public async Task ASummaryPageOfAnotherExtractionVersionBlocksBeforeAnyCall()
    {
        var world = World();
        world.SetVersion("summaries", "old-version");
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(world, model);

        Assert.Empty(model.Requests);
        Assert.Equal((State.Blocked, "extractionVersionChanged"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    private static async Task<VoteDraftRevision> FirstRevisionAsync(VoteWorld world, string? answer = null)
    {
        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(answer ?? Answer()));
        return Assert.Single(store.Revisions);
    }

    private static async Task<(FakeVoteDraftStore Store, FakeHeadlineSummaryModel Model)> RunAsync(VoteWorld world, FakeHeadlineSummaryModel model,
        VoteDraftRevision? current = null, DateTimeOffset? updatedAt = null)
    {
        var store = new FakeVoteDraftStore(world.Evidence(current, updatedAt));
        await AnalysisWorkers.VoteDrafting(store, new FakeAnalysisStatusStore(), world.Reader(), model, new VoteDraftSettings(10_000_000, concurrency: 1),
            BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);
        return (store, model);
    }
}
