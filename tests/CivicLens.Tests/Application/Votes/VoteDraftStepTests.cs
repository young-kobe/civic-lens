using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Application.Votes;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.HeadlineSummaryOutputs;
using static CivicLens.Tests.Fixtures.VoteWorld;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteDraftStepTests
{
    private const string HeadlineText = "The House voted on a concurrent resolution";
    private const string SummaryText = "The House decided whether to agree to a Senate amendment.";
    private static readonly string Drafted = Output(headline: HeadlineText, summary: SummaryText);

    [Fact]
    public async Task TheFirstDraftCallsTheModelAndWritesRevisionOneWithTheCandidateIdentityAndNoGate()
    {
        var (store, model) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Drafted));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((1, "civic-lens:analysis", HeadlineText, SummaryText), (revision.RevisionNumber, revision.AuthorSubject, revision.Headline, revision.Summary));
        Assert.Equal(("congress-house-votes", 119, 1, 100, "hconres", 14), (revision.SourceId, revision.Congress, revision.SessionNumber,
            revision.RollCallNumber, revision.BillType, revision.BillNumber));
        Assert.Equal((CandidateId, UpdatedAt), (revision.CandidateId, revision.CandidateUpdateDate));
        Assert.Single(model.Requests);
        var entry = Assert.Single(store.Entries);
        Assert.Equal((State.Succeeded, null), (entry.Status, entry.ErrorCode));
    }

    [Theory]
    [InlineData("On Passage", VoteKind.Passage)]
    [InlineData("On Motion to Recommit", VoteKind.MotionToRecommit)]
    [InlineData("On Motion to Table", VoteKind.Other)]
    [InlineData("On Motion to Concur in the Senate Amendment", VoteKind.Passage)]
    public async Task TheRevisionStoresTheKindOfTheQuestionAndCitesTheQuestionLineSoPublishNeverDerivesItAgain(string question, VoteKind kind)
    {
        var world = Create(question: question);

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Drafted));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal(kind, revision.Kind);
        Assert.Equal($"Question: {question}", world.Quote(revision.KindCitation!));
    }

    [Fact]
    public async Task ARollCallWithoutAQuestionLineGetsKindOtherAndNoCitationBecauseNothingBacksAKind()
    {
        var (store, _) = await RunAsync(Create(question: ""), new FakeHeadlineSummaryModel().Draft(Drafted));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((VoteKind.Other, null), (revision.Kind, revision.KindCitation));
    }

    [Fact]
    public async Task TheRevisionStoresTheVoteDayAndCitesTheStartLineBecausePublishedDatesMustQuoteTheirSource()
    {
        var world = Create();

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Drafted));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal(new DateOnly(2025, 4, 10), revision.VotedOn);
        Assert.Equal("Started: 2025-04-10T11:08:00-04:00", world.Quote(revision.VotedOnCitation));
    }

    [Fact]
    public async Task AMemberCastIsStoredAsItsRenderedLineShowsItSoThePublishedCastAlwaysEndsItsCitedLine()
    {
        var world = Create(members: [new(Aderholt, "Robert", "Aderholt", "  Yea ")]);

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Drafted));

        var member = Assert.Single(Assert.Single(store.Revisions).Members);
        Assert.Equal("Yea", member.Cast);
        Assert.EndsWith(", Yea", world.Quote(member.Citation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangedStartTextCallsTheModelBecauseTheStartLineIsAFactEvenWhenTheInstantIsTheSame()
    {
        var current = await FirstRevisionAsync(Create());
        var utcStart = current.Facts[0] with { Text = "Started: 2025-04-10T15:08:00Z" };
        current = current with { Facts = current.Facts.SetItem(0, utcStart) };
        var model = new FakeHeadlineSummaryModel().Draft(Drafted);

        var (store, _) = await RunAsync(Create(), model, current);

        Assert.Single(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((2, "Started: 2025-04-10T11:08:00-04:00"), (revision.RevisionNumber, revision.Facts[0].Text));
    }

    [Fact]
    public async Task AVoteWithoutAStartLineBlocksBecauseItsDayHasNoCitationToPublish()
    {
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(Create(withStartDate: false), model);

        Assert.Equal((State.Blocked, "evidenceUnavailable"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task AChangedKindAloneCopiesTheTextWithoutAModelCallBecauseTheKindComesFromCodeAndNotFromTheText()
    {
        var current = (await FirstRevisionAsync(Create())) with { Kind = VoteKind.Rule };
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(Create(), model, current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((2, VoteKind.Passage, current.Headline, current.Summary), (revision.RevisionNumber, revision.Kind, revision.Headline, revision.Summary));
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, "kindChanged", 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
    }

    [Fact]
    public async Task ConfidenceStaysInTheRunOutputAndNeverReachesTheRevision()
    {
        var (store, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output(headline: HeadlineText, summary: SummaryText, headlineConfidence: 0.77)));

        Assert.Contains("0.77", Assert.Single(store.Runs).Run.OutputJson, StringComparison.Ordinal);
        Assert.DoesNotContain("onfidence", JsonSerializer.Serialize(Assert.Single(store.Revisions)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSpeakerElectionCitesItsCandidateTotalsBecauseTheyAreItsOnlyTotals()
    {
        var world = Create(totals: """[{"candidate":"Johnson (LA)","total":218},{"candidate":"Jeffries","total":215}]""");

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Drafted));

        var facts = Assert.Single(store.Revisions).Facts;
        Assert.Equal(["Candidate total Jeffries: 215", "Candidate total Johnson (LA): 218"],
            facts.Select(fact => fact.Text).Where(text => text.StartsWith("Candidate total ", StringComparison.Ordinal)));
        Assert.DoesNotContain(facts, fact => fact.Text.StartsWith("Party total ", StringComparison.Ordinal));
        Assert.Equal(facts.Select(fact => fact.Text), facts.Select(fact => world.Quote(fact.Citation)));
    }

    [Fact]
    public async Task CitationsPointAtTheExactRenderedLinesSoAReaderCanCheckEveryFactAgainstTheSavedExtraction()
    {
        var world = Create();

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Drafted));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal([
            "Started: 2025-04-10T11:08:00-04:00",
            "Question: On Motion to Concur in the Senate Amendment",
            "Result: Passed",
            "Legislation: HCONRES 14",
            "Party total D: Yea 1, Nay 212, Present 0, Not voting 2",
            "Party total R: Yea 216, Nay 2, Present 0, Not voting 3",
            $"Title: {BillTitle}",
            "Policy area: Housing and Community Development"], revision.Facts.Select(fact => fact.Text));
        Assert.Equal(revision.Facts.Select(fact => fact.Text), revision.Facts.Select(fact => world.Quote(fact.Citation)));
        Assert.Equal([(Aderholt, "Yea", "Member: A000055, Robert Aderholt, R, AL, Yea"),
            (Auchincloss, "Nay", "Member: A000148, Jake Auchincloss, R, AL, Nay")],
            revision.Members.Select(member => (member.BioguideId, member.Cast, world.Quote(member.Citation))));
        Assert.Equal(["aderholt", "auchincloss"], revision.Members.Select(member => member.PersonId));
    }

    [Fact]
    public async Task MembersWhomNobodyTracksNeverEnterTheRecordBecauseOnlyTrackedVotesAreCited()
    {
        var (store, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Drafted));

        Assert.DoesNotContain(Amodei, Assert.Single(store.Revisions).Members.Select(member => member.BioguideId));
    }

    [Fact]
    public async Task ThePromptHoldsOnlyTheAgreedFactLinesAndNoMemberBecauseCodeOwnsTheMembersAndTheirVotes()
    {
        var (_, model) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Drafted));

        var prompt = Assert.Single(model.Requests).UserPrompt;
        Assert.Contains("Question: On Motion to Concur", prompt, StringComparison.Ordinal);
        Assert.Contains("Result: Passed", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Vote type", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Updated:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(Aderholt, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Member:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Aderholt", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Amodei", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CoverageIsCheckedOnTheVoteDayBecauseALaterCorrectionMustNotChangeWhoWasTrackedForTheVote()
    {
        var (store, model) = await RunAsync(Create(backfillFrom: new DateOnly(2025, 5, 1)), new FakeHeadlineSummaryModel());

        Assert.Empty(model.Requests);
        Assert.Equal((State.Succeeded, "noTrackedMember"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task ARollCallWithNoTrackedMemberSettlesSucceededWithoutACallOrARevision()
    {
        var model = new FakeHeadlineSummaryModel();
        var world = Create(members: [new(Amodei, "Mark", "Amodei", "Yea")]);

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Succeeded, "noTrackedMember"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, "noTrackedMember", 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
    }

    [Fact]
    public async Task ARecollectedVoteWithTheSameFactsAndVotesSettlesWithoutACallEvenWhenItsUpdateDateMoved()
    {
        var current = await FirstRevisionAsync(Create());
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(Create(), model, current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
        Assert.Equal((State.Succeeded, "unchanged"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, "unchanged", 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
    }

    [Fact]
    public async Task ACorrectedMemberVoteAppendsAtOnceWithoutAModelCallAndKeepsTheTextBecauseCodeOwnsTheVotes()
    {
        var current = await FirstRevisionAsync(Create());
        var model = new FakeHeadlineSummaryModel();
        var corrected = Create(members: [new(Aderholt, "Robert", "Aderholt", "Nay"), new(Auchincloss, "Jake", "Auchincloss", "Nay")]);

        var (store, _) = await RunAsync(corrected, model, current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((2, HeadlineText, SummaryText), (revision.RevisionNumber, revision.Headline, revision.Summary));
        Assert.Equal(["Nay", "Nay"], revision.Members.Select(member => member.Cast));
        Assert.Equal((State.Succeeded, null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, "memberVotesChanged", 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
    }

    [Fact]
    public async Task APersonsTextIsKeptAndTheModelsNewTextBecomesASuggestionBecauseAModelPredictionIsNeverAHumanEdit()
    {
        var edited = PersonEditOf(await FirstRevisionAsync(Create()), "Edited headline", "Edited summary.");
        var changed = Create(result: "Failed");

        var (store, _) = await RunAsync(changed, new FakeHeadlineSummaryModel().Draft(Output(headline: "Model headline", summary: "Model summary.")),
            edited, updatedAt: UpdatedAt.AddDays(1));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal(("Edited headline", "Edited summary.", ReviewTextOrigin.Person), (revision.Headline, revision.Summary, revision.TextOrigin));
        Assert.Equal(("Model headline", "Model summary."), (revision.Suggestion!.Headline, revision.Suggestion.Summary));
        Assert.Equal(Assert.Single(store.Runs).Run.RunId, revision.Suggestion.RunId);
        Assert.Equal("civic-lens:analysis", revision.AuthorSubject);
    }

    [Fact]
    public async Task ACorrectedMemberVoteKeepsAPersonsTextAndTheSuggestionWithoutAModelCall()
    {
        var suggestion = new ReviewTextSuggestion("Model headline", "Model summary.", new string('1', 32), new string('d', 64));
        var edited = PersonEditOf(await FirstRevisionAsync(Create()), "Edited headline", "Edited summary.") with { Suggestion = suggestion };
        var model = new FakeHeadlineSummaryModel();
        var corrected = Create(members: [new(Aderholt, "Robert", "Aderholt", "Nay"), new(Auchincloss, "Jake", "Auchincloss", "Nay")]);

        var (store, _) = await RunAsync(corrected, model, edited, updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal(("Edited headline", "Edited summary.", ReviewTextOrigin.Person, suggestion),
            (revision.Headline, revision.Summary, revision.TextOrigin, revision.Suggestion));
        Assert.Equal(["Nay", "Nay"], revision.Members.Select(member => member.Cast));
    }

    [Fact]
    public async Task AMemberVoteChangeTogetherWithAChangedResultStillCallsTheModel()
    {
        var current = await FirstRevisionAsync(Create());
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: "The House rejected a concurrent resolution", summary: SummaryText));
        var changed = Create(result: "Failed", members: [new(Aderholt, "Robert", "Aderholt", "Nay"), new(Auchincloss, "Jake", "Auchincloss", "Nay")]);

        var (store, _) = await RunAsync(changed, model, current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Single(model.Requests);
        Assert.Equal(("The House rejected a concurrent resolution", 2), (Assert.Single(store.Revisions).Headline, Assert.Single(store.Revisions).RevisionNumber));
    }

    [Fact]
    public async Task AChangedTrackedMemberSetCopiesTheTextWithoutACallBecauseTheTextNamesNoMember()
    {
        var current = await FirstRevisionAsync(Create());
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(Create(members: [new(Aderholt, "Robert", "Aderholt", "Yea")]), model, current,
            updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((current.Headline, current.Summary), (revision.Headline, revision.Summary));
        Assert.Equal(Aderholt, Assert.Single(revision.Members).BioguideId);
    }

    [Theory]
    [InlineData("Nancy Pelosi called a vote")]
    [InlineData("NANCY PELOSI called a vote")]
    [InlineData("Robert Aderholt voted Yea")]
    public async Task TextThatNamesAnyConfiguredPersonIsRejectedSoNoCopiedTextCanStateAStaleVote(string headline)
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: headline, summary: SummaryText)).Draft(Drafted);

        var (store, _) = await RunAsync(Create(), model);

        Assert.Contains("so name no person", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task ANameInTheSummaryIsRejectedToo()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: HeadlineText, summary: "Nancy Pelosi presided.")).Draft(Drafted);

        var (store, _) = await RunAsync(Create(), model);

        Assert.Contains("The summary names \"Nancy Pelosi\"", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Single(store.Revisions);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public async Task AConfidenceOutsideZeroToOneIsRejected(double confidence)
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output(headlineConfidence: confidence)).Draft(Output(summaryConfidence: 2));

        var (store, _) = await RunAsync(Create(), model);

        Assert.Equal((State.Failed, "outputRejected"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Contains("must be a number from 0 to 1", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task TextWithANulOrOverTheBoundIsRejected()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: "bad\0text"))
            .Draft(Output(summary: new string('x', VoteDraftRevision.MaximumSummaryLength + 1)));

        var (store, _) = await RunAsync(Create(), model);

        Assert.Equal((State.Failed, "outputRejected"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task AnAnswerThatIsNotTheSchemaIsRejectedLikeAnyOtherOutputError()
    {
        var model = new FakeHeadlineSummaryModel().Draft("not json").Draft(Drafted);

        var (store, _) = await RunAsync(Create(), model);

        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task ARenderedTextThatDiffersFromTheSavedExtractionBlocksBeforeAnyCall()
    {
        var world = Create();
        world.TamperWithSavedText("members", "House vote members: 119 1 100\nMember: A000055, Robert Aderholt, R, AL, Nay");
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Blocked, "renderedTextMismatch"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
    }

    [Theory]
    [InlineData("detail", "detail")]
    [InlineData("members", "members")]
    [InlineData("members", "detail")]
    public async Task AChangedParserVersionBlocksBeforeAnyCaptureIsParsedSoAnUpgradeIsNeverReportedAsBrokenData(string changedJob, string unreadableJob)
    {
        var world = Create();
        world.SetVersion(changedJob, "older-version");
        world.ReplaceCapturedBody(unreadableJob, "not json");
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Blocked, "extractionVersionChanged"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ACandidateWithANewerCandidateOfTheSameRollCallIsSupersededWithoutACall()
    {
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(Create(), model, hasNewerCandidate: true);

        Assert.Equal((State.Succeeded, "supersededByNewerCandidate"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task AFactOverTheRevisionLimitBlocksBeforeAnyCallOrBudgetReservation()
    {
        var model = new FakeHeadlineSummaryModel();
        var world = Create(question: new string('q', VoteDraftRevision.MaximumFactLength));

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Blocked, "factTooLong"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Equal(0, store.Charged);
    }

    [Fact]
    public async Task FactsThatAloneExceedTheByteCapBlockWithoutACall()
    {
        var model = new FakeHeadlineSummaryModel();
        var settings = new VoteDraftSettings(10_000_000, VoteDraftPrompt.MinimumReservation, 1);

        var (store, _) = await RunAsync(Create(), model, settings: settings);

        Assert.Equal((State.Blocked, "overByteCap"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task TheDailyBudgetIsCheckedLikeActionDraftingSoAFullDayWaits()
    {
        var world = Create();
        var store = new FakeVoteDraftStore(world.Evidence()) { Charged = 9_999_999 };
        var model = new FakeHeadlineSummaryModel();

        await AnalysisWorkers.VoteDrafting(store, new FakeAnalysisStatusStore(), world.Reader(), model,
            new VoteDraftSettings(10_000_000, concurrency: 1), BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);

        Assert.Equal((State.WaitingForBudget, "dailyTokenLimit"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task TheRevisionFollowsTheCurrentRevisionNumberAndNeverTheModelsChoice()
    {
        var current = await FirstRevisionAsync(Create());

        var (store, _) = await RunAsync(Create(result: "Failed"), new FakeHeadlineSummaryModel().Draft(Drafted), current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Equal(2, Assert.Single(store.Revisions).RevisionNumber);
    }

    [Fact]
    public async Task TheBillTitleAndPolicyAreaAreCitedFromTheSavedBillDetailSoTheModelKnowsWhatTheMeasureIs()
    {
        var world = Create();

        var (store, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Drafted));

        var facts = Assert.Single(store.Revisions).Facts.TakeLast(2).ToArray();
        Assert.Equal([$"Title: {BillTitle}", "Policy area: Housing and Community Development"], facts.Select(fact => world.Quote(fact.Citation)));
        Assert.Equal(ExtractionIdOf("bill"), facts[0].Citation.ExtractionId);
        var prompt = Assert.Single(model.Requests).UserPrompt;
        Assert.Contains($"Title: {BillTitle}", prompt, StringComparison.Ordinal);
        Assert.Contains("title of the measure", Assert.Single(model.Requests).SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABillDetailWithoutAPolicyAreaCitesTheTitleOnlyAndTracksNoIssue()
    {
        var (store, _) = await RunAsync(Create(policyArea: null), new FakeHeadlineSummaryModel().Draft(Drafted));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal($"Title: {BillTitle}", revision.Facts[^1].Text);
        Assert.DoesNotContain(revision.Facts, fact => fact.Text.StartsWith("Policy area: ", StringComparison.Ordinal));
        Assert.Empty(revision.TrackedIssueIds);
    }

    [Theory]
    [InlineData(BillGateFixtures.TrackedPolicyArea, new[] { "rent" })]
    [InlineData(BillGateFixtures.UntrackedPolicyArea, new string[0])]
    public async Task TheTrackedIssuesComeFromThePolicyAreaOfTheBillAndTheRevisionNamesTheIssueRevision(string policyArea, string[] expected)
    {
        var (store, _) = await RunAsync(Create(policyArea: policyArea), new FakeHeadlineSummaryModel().Draft(Drafted));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal(expected, revision.TrackedIssueIds);
        Assert.Equal(BillGateFixtures.Revision().Id, revision.IssueRevisionId);
    }

    [Fact]
    public async Task TheTrackedIssuesAreCheckedOnTheVoteDaySoAnIssueThatEndedBeforeTheVoteIsNotTracked()
    {
        var ended = IssueConfigurationRevision.Create(BillGateFixtures.Issues(endsBefore: new DateOnly(2025, 4, 1)));

        var (store, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Drafted), issues: ended);

        Assert.Empty(Assert.Single(store.Revisions).TrackedIssueIds);
    }

    [Fact]
    public async Task AChangedBillTitleCallsTheModelBecauseTheTitleIsAFact()
    {
        var current = await FirstRevisionAsync(Create());
        var model = new FakeHeadlineSummaryModel().Draft(Drafted);

        var (store, _) = await RunAsync(Create(billTitle: "A renamed measure"), model, current);

        Assert.Single(model.Requests);
        Assert.Equal(2, Assert.Single(store.Revisions).RevisionNumber);
    }

    [Fact]
    public async Task AnotherIssueRevisionCallsTheModelEvenWhenEveryFactIsTheSameBecauseTheTrackedIssuesCameFromOtherRules()
    {
        var current = await FirstRevisionAsync(Create());
        var model = new FakeHeadlineSummaryModel().Draft(Drafted);
        var other = IssueConfigurationRevision.Create(BillGateFixtures.Issues(startsOn: new DateOnly(2000, 1, 1)));

        var (store, _) = await RunAsync(Create(), model, current, issues: other);

        Assert.Single(model.Requests);
        Assert.Equal(other.Id, Assert.Single(store.Revisions).IssueRevisionId);
    }

    [Fact]
    public async Task ARecollectedVoteWithTheSameBillAndTheSameIssueRevisionStillSettlesUnchanged()
    {
        var current = await FirstRevisionAsync(Create());
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(Create(), model, current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        Assert.Equal("unchanged", store.Entries[0].ErrorCode);
    }

    [Fact]
    public async Task AVoteOnABillWithoutASavedBillDetailBlocksBecauseTheDraftNeedsTheTitle()
    {
        var world = Create();

        var (store, model) = await RunAsync(world, new FakeHeadlineSummaryModel(), evidence: world.Evidence() with { BillDetail = null });

        Assert.Equal((State.Blocked, "evidenceUnavailable"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task AVoteWithoutABillIdentityNeedsNoBillDetailAndCitesNoTitle()
    {
        var world = Create();
        var evidence = world.Evidence();
        evidence = evidence with { Candidate = evidence.Candidate with { BillType = null, BillNumber = null }, BillDetail = null };

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Drafted), evidence: evidence);

        var revision = Assert.Single(store.Revisions);
        Assert.DoesNotContain(revision.Facts, fact => fact.Text.StartsWith("Title: ", StringComparison.Ordinal));
        Assert.Empty(revision.TrackedIssueIds);
    }

    [Fact]
    public async Task ABillDetailOfAnOlderParserBlocksBeforeAnyCallBecauseItsLinesMayHaveAnotherMeaning()
    {
        var world = Create();
        world.SetVersion("bill", "congress-bill-v0");

        var (store, model) = await RunAsync(world, new FakeHeadlineSummaryModel());

        Assert.Equal((State.Blocked, "extractionVersionChanged"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ASavedBillDetailThatDiffersFromTheRenderOfItsCaptureBlocksSoTheCitedLinesAreNeverTrustedBlindly()
    {
        var world = Create();
        world.TamperWithSavedText("bill", "Bill: 119 hconres 14\nTitle: Changed after capture");

        var (store, model) = await RunAsync(world, new FakeHeadlineSummaryModel());

        Assert.Equal((State.Blocked, "renderedTextMismatch"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    private static VoteDraftRevision PersonEditOf(VoteDraftRevision previous, string headline, string summary) => previous with
    {
        RevisionNumber = previous.RevisionNumber + 1,
        AuthorSubject = "auth0|editor",
        Headline = headline,
        Summary = summary,
        TextOrigin = ReviewTextOrigin.Person
    };

    private static async Task<VoteDraftRevision> FirstRevisionAsync(VoteWorld world)
    {
        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Drafted));
        return Assert.Single(store.Revisions);
    }

    private static async Task<(FakeVoteDraftStore Store, FakeHeadlineSummaryModel Model)> RunAsync(VoteWorld world, FakeHeadlineSummaryModel model,
        VoteDraftRevision? current = null, DateTimeOffset? updatedAt = null, VoteDraftSettings? settings = null, bool hasNewerCandidate = false,
        IssueConfigurationRevision? issues = null, VoteDraftEvidence? evidence = null)
    {
        var store = new FakeVoteDraftStore(evidence ?? world.Evidence(current, updatedAt, hasNewerCandidate));
        await AnalysisWorkers.VoteDrafting(store, new FakeAnalysisStatusStore(), world.Reader(), model,
            settings ?? new VoteDraftSettings(10_000_000, concurrency: 1), issues ?? BillGateFixtures.Revision(), TimeProvider.System)
            .ExecuteAsync(once: true);
        return (store, model);
    }
}
