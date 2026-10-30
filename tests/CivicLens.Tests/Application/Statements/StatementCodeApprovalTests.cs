using CivicLens.Application.Collection;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementCodeApprovalTests
{
    private static readonly DateTimeOffset BothCovered = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static CollectionConfigurationRevision OwnOfficeOf(string personId) => CollectionConfigurationRevision.Create(OwnOffice(personId));

    [Fact]
    public async Task ADraftOfTheOwnOfficeSourceThatPassesEveryCheckIsApprovedByCode()
    {
        var store = await DraftAsync(DraftEvidence(configuration: OwnOfficeOf(Warren)), Output());

        Assert.Equal([store.Evidence.Article.CandidateId], store.CodeApproved);
    }

    [Fact]
    public async Task ADraftFromASourceWithoutOfficeOfWaitsForAPersonBecauseCoverageIsNotAuthorship()
    {
        var store = await DraftAsync(DraftEvidence(configuration: SpeakerRevision()), Output());

        Assert.Empty(store.CodeApproved);
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task ADraftFromTheOfficeOfAnotherPersonWaitsForAPersonBecauseTheSpeakerIsNotTheOwner()
    {
        var shared = Configuration();
        var markeysOffice = CollectionConfigurationRevision.Create(shared with { Sources = [shared.Sources[0] with { OfficeOf = Markey }] });

        var store = await DraftAsync(DraftEvidence(configuration: markeysOffice), Output());

        Assert.Empty(store.CodeApproved);
    }

    [Fact]
    public async Task ADraftWithANamedOtherSpeakerWaitsForAPerson()
    {
        var store = await DraftAsync(DraftEvidence(configuration: OwnOfficeOf(Warren)), Output(otherSpeakers: ["Jo Rivera"]));

        Assert.Empty(store.CodeApproved);
    }

    [Fact]
    public async Task ADraftThatNamesAnotherCoveredOfficialAsSpeakerWaitsForAPerson()
    {
        var evidence = DraftEvidence(observed: BothCovered, configuration: OwnOfficeOf(Warren));

        var store = await DraftAsync(evidence, Output(speakers: [Warren, Markey]));

        Assert.Empty(store.CodeApproved);
    }

    [Fact]
    public async Task ADraftWithAnAiProposedDateWaitsForAPersonBecauseTheDateIsNotFromTheSource()
    {
        var store = await DraftAsync(DraftEvidence(configuration: OwnOfficeOf(Warren)),
            Output(date: ("2026-10-03", "WASHINGTON, October 3, 2026")));

        Assert.Empty(store.CodeApproved);
        Assert.NotNull(Assert.Single(store.Revisions).StatementDate);
    }

    [Theory]
    [InlineData(StatementGateReasons.FailedOpen)]
    [InlineData(StatementGateReasons.OverByteCap)]
    public async Task ADraftQueuedAfterAGateThatVerifiedNothingWaitsForAPerson(string reason)
    {
        var carryOver = StatementGateCarryOver.From(GateRunOf(reason));

        var store = await DraftAsync(DraftEvidence(carryOver: carryOver, configuration: OwnOfficeOf(Warren)), Output());

        Assert.Empty(store.CodeApproved);
    }

    [Fact]
    public void ADraftIssueWithoutAVerifiedGateQuoteIsNotApprovedByCode()
    {
        var evidence = DraftEvidence(carryOver: new([], [StatementGateFixtures.HousingArea]), configuration: OwnOfficeOf(Warren));
        var revision = StatementDraftResolver.Resolve(Output(), evidence, Officials(Warren), DateTimeOffset.UnixEpoch).Revision! with
        {
            IssueIds = [StatementGateFixtures.RentIssue]
        };

        Assert.False(StatementCodeApproval.Approves(evidence, Allowed(), revision));
    }

    [Fact]
    public void ARuleThatAPersonApprovalEnforcesAlsoBlocksCodeApproval()
    {
        var evidence = DraftEvidence(configuration: OwnOfficeOf(Warren));
        var revision = StatementDraftResolver.Resolve(Output(), evidence, Officials(Warren), DateTimeOffset.UnixEpoch).Revision!;
        var narrower = new StatementAllowedSelections([], [StatementGateFixtures.RentIssue], [StatementGateFixtures.HousingArea]);

        Assert.False(StatementCodeApproval.Approves(evidence, narrower, revision));
    }

    private static StatementAllowedSelections Allowed() => new([Warren], [StatementGateFixtures.RentIssue], [StatementGateFixtures.HousingArea]);

    private static async Task<FakeStatementDraftStore> DraftAsync(StatementDraftEvidence evidence, string output)
    {
        var store = new FakeStatementDraftStore(evidence);
        var model = new FakeStatementDraftModel().Draft(output);
        await AnalysisWorkers.StatementDrafting(store, new FakeAnalysisStatusStore(), model, new StatementDraftSettings(10_000_000, concurrency: 1),
            new FakeClock(DateTimeOffset.UnixEpoch)).ExecuteAsync(once: true);
        return store;
    }

    private static StatementGateRun GateRunOf(string reason)
    {
        var article = StatementGateFixtures.Evidence(Article);
        var run = new AnalysisRun(new string('a', 32), article.CandidateId, StatementGateTask.Task, StatementGateTask.TaskVersion,
            StatementGateTask.Model, StatementGateTask.PromptVersion, StatementGateTask.SchemaVersion, article.Extraction.TextSha256, null,
            AnalysisRunOutcome.Classified, AnalysisTokenUsage.None, 0, "end_turn", null, StatementGateFixtures.Output([StatementGateFixtures.HousingArea], 0.8, (StatementGateFixtures.RentIssue, "a rent cap")), [],
            "{}", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        return new(run, StatementGateFixtures.Revision().Id, reason, [new(StatementGateFixtures.RentIssue, article.Extraction.ExtractionId, 0, 3)]);
    }
}
