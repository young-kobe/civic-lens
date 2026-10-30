using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Core.Statements;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillWorld;
using static CivicLens.Tests.Fixtures.HeadlineSummaryOutputs;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionDraftStepTests
{
    private static string Output(string headline = "Warren sponsored a housing bill", string summary = "Elizabeth Warren introduced a housing bill on January 3, 2025.",
        double headlineConfidence = 0.8, double summaryConfidence = 0.7, string[]? issueIds = null) =>
        HeadlineSummaryOutputs.Output(headline, summary, headlineConfidence, summaryConfidence, issueIds ?? ["rent"]);

    private static readonly string UnnamedOutput = Output(headline: "A housing bill", summary: "A housing bill was introduced.");
    private static readonly CosponsorSpec MarkeyOriginal = new(Markey, "2025-01-04", Original: true);
    private static readonly SummarySpec Summary = new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>");

    [Fact]
    public async Task TheFirstDraftAlwaysCallsTheModelAndWritesRevisionOneWithTheFingerprintAndNoSuggestion()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]], summaries: [Summary]);
        var (store, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((1, "civic-lens:analysis", ReviewTextOrigin.Model, null), (revision.RevisionNumber, revision.AuthorSubject, revision.TextOrigin, revision.Suggestion));
        Assert.Equal(("congress-bills", 119, "hr", 1), (revision.SourceId, revision.Congress, revision.BillType, revision.BillNumber));
        Assert.Equal(new DateTimeOffset(2025, 3, 3, 12, 0, 0, TimeSpan.Zero), revision.CandidateUpdateDate);
        Assert.Matches("^[0-9a-f]{64}$", revision.Fingerprint);
        Assert.Equal("00@2025-01-05T10:00:00Z", revision.SummaryVersion);
        Assert.Equal(["rent"], revision.TrackedIssueIds.ToArray());
        Assert.Equal(["rent"], revision.IssueCandidateIds.ToArray());
        Assert.Equal("housing", revision.PolicyAreaId);
        Assert.Equal(BillGateFixtures.Revision().Id, revision.IssueRevisionId);
        Assert.Single(model.Requests);
        var entry = Assert.Single(store.Entries);
        Assert.Equal((State.Succeeded, null), (entry.Status, entry.ErrorCode));
    }

    [Fact]
    public async Task APolicyAreaLabelThatTheConfigurationLacksStoresNoAreaIDSoThePublishedFilterNeverGuesses()
    {
        var (store, _) = await RunAsync(Create(policyArea: "Public Lands"), new FakeHeadlineSummaryModel().Draft(Output(issueIds: [])));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal(("Public Lands", null), (revision.PolicyArea, revision.PolicyAreaId));
    }

    [Fact]
    public async Task ADifferentConfiguredAreaIDDraftsAgainBecauseTheStoredIDIsPartOfWhatTheReviewerApproved()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]]);
        var (first, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));
        var staleAreaId = Assert.Single(first.Revisions) with { PolicyAreaId = "old-housing" };

        var (second, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()), staleAreaId);

        Assert.Single(model.Requests);
        Assert.Equal("housing", Assert.Single(second.Revisions).PolicyAreaId);
    }

    [Fact]
    public async Task ConfidenceStaysInTheRunOutputAndNeverReachesTheRevision()
    {
        var (store, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output(headlineConfidence: 0.77)));

        Assert.Contains("0.77", Assert.Single(store.Runs).Run.OutputJson, StringComparison.Ordinal);
        Assert.DoesNotContain("onfidence", JsonSerializer.Serialize(Assert.Single(store.Revisions)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CitationsPointAtTheExactRenderedLinesOfTheSavedExtractions()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]], summaries: [Summary]);
        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

        var revision = Assert.Single(store.Revisions);
        var warren = revision.Sponsorships.Single(item => item.BioguideId == Warren);
        Assert.Equal(["Sponsor: W000817", "Introduced: 2025-01-03"], warren.Citations.Select(world.Quote));
        var markey = revision.Sponsorships.Single(item => item.BioguideId == Markey);
        Assert.Equal(["Cosponsor: M000133, original, 2025-01-04"], markey.Citations.Select(world.Quote));
        Assert.Equal($"Policy area: {BillGateFixtures.TrackedPolicyArea}", world.Quote(revision.PolicyAreaCitation!));
        Assert.Equal("Summary version: 00\nAction date: 2025-01-03\nAction: Introduced\nUpdated: 2025-01-05T10:00:00Z\nText:\nText of <p>It funds rent aid.</p>",
            world.Quote(revision.SummaryCitation!));
    }

    [Fact]
    public async Task AWithdrawnCosponsorshipIsListedAndCitedWithBothDates()
    {
        var world = Create(sponsors: [Stranger], cosponsorPages: [[new(Markey, "2025-01-04", Original: false, Withdrawn: "2025-02-10")]]);
        var (store, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(UnnamedOutput));

        var markey = Assert.Single(Assert.Single(store.Revisions).Sponsorships);
        Assert.Equal((new DateOnly(2025, 1, 4), new DateOnly(2025, 2, 10)), (markey.SponsoredOn, markey.WithdrawnOn));
        Assert.Equal("Cosponsor: M000133, 2025-01-04, withdrawn 2025-02-10", world.Quote(Assert.Single(markey.Citations)));
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task ThePromptNamesOnlyTheSponsorsBecauseCodeOwnsTheCosponsorListThatSponsorshipChangesUpdate()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal with { Withdrawn = "2025-02-10" }]]);

        var (_, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

        var prompt = Assert.Single(model.Requests).UserPrompt;
        Assert.Contains("Elizabeth Warren", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Markey", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("2025-02-10", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("cosponsor", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MembersWhomNobodyTracksNeverEnterTheFactsTheFingerprintOrTheRecord()
    {
        var withStranger = Create(sponsors: [Warren, Stranger], cosponsorPages: [[new(Stranger, "2025-01-09"), MarkeyOriginal]]);
        var withoutStranger = Create(sponsors: [Warren], cosponsorPages: [[MarkeyOriginal]]);

        var (store, model) = await RunAsync(withStranger, new FakeHeadlineSummaryModel().Draft(Output()));
        var (other, _) = await RunAsync(withoutStranger, new FakeHeadlineSummaryModel().Draft(Output()));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal([Markey, Warren], revision.Sponsorships.Select(item => item.BioguideId).Order());
        Assert.DoesNotContain(Stranger, model.Requests[0].UserPrompt, StringComparison.Ordinal);
        Assert.Equal(Assert.Single(other.Revisions).Fingerprint, revision.Fingerprint);
    }

    [Fact]
    public async Task ABillWithNoTrackedOfficialSettlesSucceededWithoutACallOrARevision()
    {
        var model = new FakeHeadlineSummaryModel();
        var (store, _) = await RunAsync(Create(sponsors: [Stranger]), model);

        Assert.Equal((State.Succeeded, "noTrackedOfficial"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, "noTrackedOfficial", 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
    }

    [Fact]
    public async Task AMatchingFingerprintAndSummaryVersionSettleWithoutAModelCall()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]], summaries: [Summary]);
        var (first, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));
        var current = Assert.Single(first.Revisions);
        var model = new FakeHeadlineSummaryModel();

        var (second, _) = await RunAsync(world, model, current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        Assert.Empty(second.Revisions);
        var entry = Assert.Single(second.Entries);
        Assert.Equal((State.Succeeded, "unchanged"), (entry.Status, entry.ErrorCode));
        var run = Assert.Single(second.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, "unchanged", 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
    }

    [Fact]
    public async Task ANewSponsorshipAppendsAtOnceWithoutAModelCallAndKeepsTheTextBecauseCodeOwnsTheSponsorshipList()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var current = Assert.Single(first.Revisions);
        var model = new FakeHeadlineSummaryModel();

        var (second, _) = await RunAsync(Create(cosponsorPages: [[MarkeyOriginal]]), model, current, updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        var revision = Assert.Single(second.Revisions);
        Assert.Equal((2, current.Headline, current.Summary, ReviewTextOrigin.Model, null),
            (revision.RevisionNumber, revision.Headline, revision.Summary, revision.TextOrigin, revision.Suggestion));
        Assert.Equal([Markey, Warren], revision.Sponsorships.Select(item => item.BioguideId).Order());
        Assert.NotEqual(current.Fingerprint, revision.Fingerprint);
        Assert.Equal((State.Succeeded, null), (second.Entries[0].Status, second.Entries[0].ErrorCode));
        var run = Assert.Single(second.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, "sponsorshipsChanged", 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
    }

    [Fact]
    public async Task ASponsorshipChangeKeepsAPersonsTextAndTheSuggestionUnchangedWithItsRunId()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var suggestion = new ReviewTextSuggestion("Model headline", "Model summary.", Assert.Single(first.Runs).Run.RunId,
            Assert.Single(first.Revisions).Fingerprint);
        var edited = ActionRevisionFixtures.PersonEditOf(Assert.Single(first.Revisions), "Edited headline", "Edited summary.") with
        {
            Suggestion = suggestion
        };
        var model = new FakeHeadlineSummaryModel();

        var (second, _) = await RunAsync(Create(cosponsorPages: [[MarkeyOriginal]]), model, edited, updatedAt: UpdatedAt.AddDays(1));

        Assert.Empty(model.Requests);
        var revision = Assert.Single(second.Revisions);
        Assert.Equal(("Edited headline", "Edited summary.", ReviewTextOrigin.Person, suggestion),
            (revision.Headline, revision.Summary, revision.TextOrigin, revision.Suggestion));
        Assert.Equal("civic-lens:analysis", revision.AuthorSubject);
    }

    [Fact]
    public async Task ADifferentWithdrawalDateChangesTheFingerprintAndAppendsWithoutAModelCall()
    {
        var active = Create(sponsors: [Stranger], cosponsorPages: [[MarkeyOriginal]]);
        var withdrawn = Create(sponsors: [Stranger], cosponsorPages: [[MarkeyOriginal with { Withdrawn = "2025-02-10" }]]);

        var (first, _) = await RunAsync(active, new FakeHeadlineSummaryModel().Draft(UnnamedOutput));
        var (second, model) = await RunAsync(withdrawn, new FakeHeadlineSummaryModel(), Assert.Single(first.Revisions), updatedAt: UpdatedAt.AddDays(1));

        Assert.Equal(new DateOnly(2025, 2, 10), Assert.Single(Assert.Single(second.Revisions).Sponsorships).WithdrawnOn);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ANewSponsorCallsTheModelBecauseTheTextNamesTheSponsors()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: "Warren and Markey sponsor a bill"));

        var (second, _) = await RunAsync(Create(sponsors: [Warren, Markey]), model, Assert.Single(first.Revisions), updatedAt: UpdatedAt.AddDays(1));

        Assert.Single(model.Requests);
        Assert.Equal("Warren and Markey sponsor a bill", Assert.Single(second.Revisions).Headline);
    }

    [Fact]
    public async Task ASponsorshipChangeTogetherWithAnotherFactStillCallsTheModel()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: "Warren and Markey back a renamed bill"));

        var (second, _) = await RunAsync(Create(cosponsorPages: [[MarkeyOriginal]], title: "A renamed housing bill"), model,
            Assert.Single(first.Revisions), updatedAt: UpdatedAt.AddDays(1));

        Assert.Single(model.Requests);
        Assert.Equal("Warren and Markey back a renamed bill", Assert.Single(second.Revisions).Headline);
    }

    [Fact]
    public async Task ANewerSummaryDraftsAgainEvenWhenNoSponsorshipChanged()
    {
        var older = Create(summaries: [Summary]);
        var newer = Create(summaries: [Summary, new SummarySpec("01", "2025-02-01", "2025-02-02T10:00:00Z", "<p>Amended.</p>")]);
        var (first, _) = await RunAsync(older, new FakeHeadlineSummaryModel().Draft(Output()));

        var (second, model) = await RunAsync(newer, new FakeHeadlineSummaryModel().Draft(Output(headline: "Revised")), Assert.Single(first.Revisions));

        Assert.Equal("01@2025-02-02T10:00:00Z", Assert.Single(second.Revisions).SummaryVersion);
        Assert.Contains("Amended.", model.Requests[0].UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("It funds rent aid.", model.Requests[0].UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheNewestSummaryGoesToTheModelByActionDateThenUpdateTimeThenVersionCode()
    {
        var world = Create(summaries:
        [
            new("02", "2025-02-01", "2025-02-02T10:00:00Z", "<p>Second.</p>"),
            new("03", "2025-02-01", "2025-02-02T10:00:00Z", "<p>Third.</p>"),
            new("09", "2025-01-01", "2025-09-02T10:00:00Z", "<p>Old action.</p>")
        ]);

        var (_, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.Contains("Third.", model.Requests[0].UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Second.", model.Requests[0].UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Old action.", model.Requests[0].UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheModelGetsNoLatestActionAndNoOtherFactsThanTheAgreedOnes()
    {
        var (_, model) = await RunAsync(Create(summaries: [Summary]), new FakeHeadlineSummaryModel().Draft(Output()));

        using var document = JsonDocument.Parse(model.Requests[0].UserPrompt["Facts:\n".Length..]);
        Assert.Equal(["bill", "candidateIssues", "sponsors", "summary"], document.RootElement.EnumerateObject().Select(item => item.Name));
    }

    [Fact]
    public async Task ARenderedTextThatDiffersFromTheSavedExtractionBlocksBeforeAnyCall()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]]);
        world.TamperWithSavedText("cosponsors-0", "Bill: 119 hr 1\nCosponsor: M000133, 2025-01-04");
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Blocked, "renderedTextMismatch"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task ACandidateWithANewerCandidateOfTheSameBillIsSupersededWithoutACall()
    {
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(Create(), model, hasNewerCandidate: true);

        Assert.Equal((State.Succeeded, "supersededByNewerCandidate"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
        Assert.Empty(store.Runs);
    }

    [Fact]
    public async Task MoreThanTwentySummaryPagesDraftWithoutSummariesAndNeverReadThemBecauseSummariesAreOptionalInput()
    {
        var summaries = Enumerable.Range(0, 21).Select(index => new SummarySpec($"{index:00}", "2025-01-03", "2025-01-05T10:00:00Z", "<p>x</p>")).ToArray();
        var world = Create(summaries: summaries);
        for (var index = 0; index < summaries.Length; index++) world.ReplaceCapturedBody($"summaries-{index}", "not json");
        world.SetVersion("summaries-0", "older-version");
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Succeeded, (string?)null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((null, null), (revision.SummaryVersion, revision.SummaryCitation));
        Assert.True(revision.CandidateIncludesSummaries);
        Assert.DoesNotContain("versionCode", Assert.Single(model.Requests).UserPrompt, StringComparison.Ordinal);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Drafted, "summaryOmitted"), (run.Run.Outcome, run.Reason));
    }

    [Fact]
    public async Task TwentySummaryPagesAreStillRead()
    {
        var summaries = Enumerable.Range(0, 20).Select(index => new SummarySpec($"{index:00}", "2025-01-03", "2025-01-05T10:00:00Z", "<p>x</p>")).ToArray();

        var (store, _) = await RunAsync(Create(summaries: summaries), new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task APersonsTextIsKeptAndTheModelsNewTextBecomesASuggestion()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]], title: "A renamed housing bill");
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var edited = ActionRevisionFixtures.PersonEditOf(Assert.Single(first.Revisions), "Edited headline", "Edited summary.");

        var (second, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output(headline: "Model headline", summary: "Model summary.")), edited);

        var revision = Assert.Single(second.Revisions);
        Assert.Equal(("Edited headline", "Edited summary.", ReviewTextOrigin.Person), (revision.Headline, revision.Summary, revision.TextOrigin));
        Assert.Equal(("Model headline", "Model summary."), (revision.Suggestion!.Headline, revision.Suggestion.Summary));
        Assert.Equal(Assert.Single(second.Runs).Run.RunId, revision.Suggestion.RunId);
        Assert.Equal(revision.Fingerprint, revision.Suggestion.Fingerprint);
        Assert.Equal(["M000133", "W000817"], revision.Sponsorships.Select(item => item.BioguideId).Order());
    }

    [Fact]
    public async Task ALaterRunStillKeepsThePersonsTextBecauseTheTextOriginTravelsWithTheRevision()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var edited = ActionRevisionFixtures.PersonEditOf(Assert.Single(first.Revisions), "Edited headline", "Edited summary.");
        var (second, _) = await RunAsync(Create(title: "One title"), new FakeHeadlineSummaryModel().Draft(Output(headline: "One")), edited);
        var keptByAnalysis = Assert.Single(second.Revisions);
        Assert.Equal("civic-lens:analysis", keptByAnalysis.AuthorSubject);

        var third = Create(title: "Two title");
        var (last, _) = await RunAsync(third, new FakeHeadlineSummaryModel().Draft(Output(headline: "Two", summary: "Two.")), keptByAnalysis);

        var revision = Assert.Single(last.Revisions);
        Assert.Equal(("Edited headline", "Edited summary.", 4), (revision.Headline, revision.Summary, revision.RevisionNumber));
        Assert.Equal("Two", revision.Suggestion!.Headline);
    }

    [Theory]
    [InlineData("Ed Markey cosponsored a bill")]
    [InlineData("ED MARKEY cosponsored a bill")]
    [InlineData("Backed by bernie sanders")]
    public async Task TextThatNamesAConfiguredPersonWhoIsNotInTheFactsIsRejectedAndTheRetryRuns(string headline)
    {
        var world = Create();
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: headline)).Draft(Output());

        var (store, _) = await RunAsync(world, model);

        Assert.Contains("who is not an official in the facts", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task ANameInTheSummaryIsRejectedToo()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output(summary: "Elizabeth Warren sponsored it with Ed Markey.")).Draft(Output());

        var (store, _) = await RunAsync(Create(), model);

        Assert.Contains("The summary names \"Ed Markey\"", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task ASponsorMayBeNamedAndAPartOfTheirNameIsNotAnotherPerson()
    {
        var (store, _) = await RunAsync(Create(sponsors: [Warren], cosponsorPages: [[MarkeyOriginal]]),
            new FakeHeadlineSummaryModel().Draft(Output(summary: "Elizabeth Warren sponsored it. Warren is a senator.")));

        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task APersonWhoSponsorsAndCosponsorsMayBeNamedBecauseOnlyAPureCosponsorBelongsToCode()
    {
        var world = Create(sponsors: [Warren, Markey], cosponsorPages: [[MarkeyOriginal]]);

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output(summary: "Ed Markey sponsored it.")));

        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task ASponsorWhoseNameContainsACosponsorsFullNameMayBeNamed()
    {
        var world = Create(sponsors: [ExtraBioguide(10)], cosponsorPages: [[new(ExtraBioguide(1), "2025-01-04", Original: true)]], extraPeople: 10);

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output(summary: "Extra Person 10 sponsored it.")));

        Assert.Single(store.Revisions);
    }

    [Theory]
    [InlineData("Ed Markey cosponsored a bill")]
    [InlineData("Backed by ED MARKEY")]
    public async Task TextThatNamesATrackedCosponsorIsRejectedBecauseCodeOwnsTheCosponsorList(string headline)
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: headline)).Draft(Output());

        var (store, _) = await RunAsync(Create(sponsors: [Warren], cosponsorPages: [[MarkeyOriginal]]), model);

        Assert.Contains("The headline names \"Ed Markey\", a cosponsor", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
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
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: "bad\0text")).Draft(Output(summary: new string('x', 8_001)));

        var (store, _) = await RunAsync(Create(), model);

        Assert.Equal((State.Failed, "outputRejected"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task AnAnswerThatIsNotTheSchemaIsRejectedLikeAnyOtherOutputError()
    {
        var model = new FakeHeadlineSummaryModel().Draft("not json").Draft(Output());

        var (store, _) = await RunAsync(Create(), model);

        Assert.Equal(store.Runs[0].Run.RunId, store.Runs[1].Run.PreviousRunId);
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task APromptOverTheByteCapDraftsFromTheStructuredFactsAloneAndRecordsItOnTheRun()
    {
        var text = new string('x', 150_000);
        var big = Create(summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>" + text + "</p>")]);
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var (store, _) = await RunAsync(big, model);

        var request = Assert.Single(model.Requests);
        Assert.DoesNotContain("xxxxxxxxxx", request.UserPrompt, StringComparison.Ordinal);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal("00@2025-01-05T10:00:00Z", revision.SummaryVersion);
        Assert.StartsWith("Summary version: 00", big.Quote(revision.SummaryCitation!), StringComparison.Ordinal);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Drafted, "summaryOmitted"), (run.Run.Outcome, run.Reason));
    }

    [Fact]
    public async Task RejectedRunsWithoutTheSummaryKeepTheReasonThroughTheRetryAndTheFailure()
    {
        var big = Create(summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>" + new string('x', 150_000) + "</p>")]);
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: "Ed Markey cosponsored a bill")).Draft(Output(headline: "Ed Markey cosponsored a bill"));

        var (store, _) = await RunAsync(big, model);

        Assert.Equal((State.Failed, "outputRejected"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Equal([(AnalysisRunOutcome.OutputRejected, "summaryOmitted"), (AnalysisRunOutcome.OutputRejected, "summaryOmitted")],
            store.Runs.Select(run => (run.Run.Outcome, run.Reason)));
    }

    [Fact]
    public async Task OnlyTheRunWhosePromptDroppedTheSummaryCarriesTheReason()
    {
        var world = Create(summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>" + new string('x', 2_000) + "</p>")]);
        var (_, probe) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));
        var justFits = new ActionDraftSettings(probe.Requests[0].ReservedTokens + 10, 1);
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: "Ed Markey cosponsored a bill")).Draft(Output(headline: "Ed Markey cosponsored a bill"));

        var (store, _) = await RunAsync(world, model, settings: justFits);

        Assert.Contains("xxxxxxxxxx", model.Requests[0].UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("xxxxxxxxxx", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([null, "summaryOmitted"], store.Runs.Select(run => run.Reason));
    }

    [Fact]
    public async Task ASummaryThatFitsIsNeverOmittedAndLeavesNoReasonOnTheRun()
    {
        var (store, model) = await RunAsync(Create(summaries: [Summary]), new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.Contains("It funds rent aid.", model.Requests[0].UserPrompt, StringComparison.Ordinal);
        Assert.Null(Assert.Single(store.Runs).Reason);
    }

    [Fact]
    public async Task FactsThatAloneExceedTheByteCapStillBlockWithoutACall()
    {
        var model = new FakeHeadlineSummaryModel();
        var settings = new ActionDraftSettings(ActionDraftPrompt.MinimumReservation, 1);

        var (store, _) = await RunAsync(Create(summaries: [Summary]), model, settings: settings);

        Assert.Equal((State.Blocked, "overByteCap"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task MoreThanSixHundredSponsorshipsBlockBeforeAnyCallOrBudgetReservation()
    {
        var pages = new[] { 1..250, 251..500, 501..601 }.Select(range =>
            Enumerable.Range(range.Start.Value, range.End.Value - range.Start.Value + 1).Select(index => new CosponsorSpec(ExtraBioguide(index), "2025-01-04")).ToArray()).ToArray();
        var world = Create(sponsors: [Stranger], cosponsorPages: pages, extraPeople: 601);
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Blocked, "tooManySponsorships"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Equal(0, store.Charged);
    }

    [Fact]
    public async Task ATitleOverTheRevisionLimitBlocksBeforeAnyCallOrBudgetReservation()
    {
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(Create(title: new string('t', ActionDraftRevision.MaximumTitleLength + 1)), model);

        Assert.Equal((State.Blocked, "titleTooLong"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
        Assert.Equal(0, store.Charged);
    }

    [Fact]
    public async Task ATitleAtTheRevisionLimitIsStillDrafted()
    {
        var (store, _) = await RunAsync(Create(title: new string('t', ActionDraftRevision.MaximumTitleLength)), new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task AModelThatPicksOneOfTwoCandidatesStoresOnlyThatIssueAndBothCandidates()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output(issueIds: ["eviction"]));

        var (store, _) = await RunAsync(Create(), model, issues: BillGateFixtures.Revision(withEviction: true));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal(["eviction"], revision.TrackedIssueIds.ToArray());
        Assert.Equal(["eviction", "rent"], revision.IssueCandidateIds.ToArray());
    }

    [Fact]
    public async Task AModelThatPicksNoIssueLeavesTheRecordWithoutOneAndNeverFallsBackToThePolicyAreaSet()
    {
        var (store, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output(issueIds: [])));

        var revision = Assert.Single(store.Revisions);
        Assert.Empty(revision.TrackedIssueIds);
        Assert.Equal(["rent"], revision.IssueCandidateIds.ToArray());
    }

    [Fact]
    public async Task ASingleCandidateIsStillOfferedToTheModelBecauseThePolicyAreaAloneDoesNotMakeAnIssueFit()
    {
        var (_, model) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));

        var request = Assert.Single(model.Requests);
        Assert.Contains("\"issueIds\"", request.SchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"rent\"", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APolicyAreaWithNoIssueSendsTheSchemaWithoutIssueIds()
    {
        var (_, model) = await RunAsync(Create(policyArea: BillGateFixtures.UntrackedPolicyArea), new FakeHeadlineSummaryModel().Draft(Output(issueIds: [])));

        Assert.DoesNotContain("issueIds", Assert.Single(model.Requests).SchemaJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABillThatChangedItsCandidateSetDraftsAgainEvenWhenTheChosenIssuesStayTheSame()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var previous = Assert.Single(first.Revisions) with { IssueCandidateIds = ["eviction", "rent"] };

        var (second, _) = await RunAsync(Create(), model, previous);

        Assert.Equal(["rent"], Assert.Single(second.Revisions).IssueCandidateIds.ToArray());
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task ARevisionThatLacksTheCandidateSetDraftsAgainBecauseItPredatesTheModelChoice()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var old = Assert.Single(first.Revisions) with { IssueCandidateIds = default };
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        await RunAsync(Create(), model, old);

        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task AChoiceOutsideTheCandidatesOrRepeatedIsRejectedWithItsReasonAndRetriedOnce()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output(issueIds: ["rent", "rent", "firearms"])).Draft(Output(issueIds: ["rent", "rent"]));

        var (store, _) = await RunAsync(Create(), model);

        Assert.Empty(store.Revisions);
        Assert.Contains("\"firearms\", which is not a candidate issue", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"rent\" more than once", model.Requests[1].UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerWithoutIssueIdsIsRejectedWhenCandidatesExist()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Output())!.AsObject();
        json.Remove("issueIds");
        var model = new FakeHeadlineSummaryModel().Draft(json.ToJsonString()).Draft(Output());

        var (store, _) = await RunAsync(Create(), model);

        Assert.Contains("issueIds is required", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task ARenamedIssueWithTheSameIdDraftsAgainBecauseTheRevisionMustNameTheIssueConfigurationItUsed()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var original = BillGateFixtures.Issues();
        var renamed = IssueConfigurationRevision.Create(original with { Issues = [original.Issues[0] with { Name = "Rent and housing costs" }] });
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var (second, _) = await RunAsync(Create(), model, Assert.Single(first.Revisions), issues: renamed);

        var revision = Assert.Single(second.Revisions);
        Assert.Equal(["rent"], revision.TrackedIssueIds.ToArray());
        Assert.Equal(renamed.Id, revision.IssueRevisionId);
        Assert.Contains("Rent and housing costs", Assert.Single(model.Requests).UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABioguideIdMappedToAnotherPersonDraftsAgainBecauseTheRecordNamesOfficialsByPersonId()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var (second, _) = await RunAsync(Create(warrenPersonId: "elizabeth-warren"), model, Assert.Single(first.Revisions));

        Assert.Equal("elizabeth-warren", Assert.Single(Assert.Single(second.Revisions).Sponsorships).PersonId);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task ABillTitleThatChangedDraftsAgainEvenWhenNothingElseChanged()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));

        var (second, model) = await RunAsync(Create(title: "A renamed housing bill"), new FakeHeadlineSummaryModel().Draft(Output()), Assert.Single(first.Revisions));

        Assert.Equal("A renamed housing bill", Assert.Single(second.Revisions).BillTitle);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task APolicyAreaAssignedLaterDraftsAgain()
    {
        var (first, _) = await RunAsync(Create(policyArea: null), new FakeHeadlineSummaryModel().Draft(Output(issueIds: [])));
        Assert.Null(Assert.Single(first.Revisions).PolicyArea);

        var (second, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()), Assert.Single(first.Revisions));

        var revision = Assert.Single(second.Revisions);
        Assert.Equal((BillGateFixtures.TrackedPolicyArea, 2), (revision.PolicyArea, revision.RevisionNumber));
    }

    [Fact]
    public async Task AChangeInTheTrackedIssuesOfTheIssueConfigurationDraftsAgain()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var inactive = IssueConfigurationRevision.Create(BillGateFixtures.Issues(startsOn: new DateOnly(2030, 1, 1)));

        var (second, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output(issueIds: [])), Assert.Single(first.Revisions), issues: inactive);

        var revision = Assert.Single(second.Revisions);
        Assert.Empty(revision.TrackedIssueIds);
        Assert.Empty(revision.IssueCandidateIds);
        Assert.Equal(inactive.Id, revision.IssueRevisionId);
    }

    [Fact]
    public async Task TheSameTitlePolicyAreaAndIssuesStillSettleAsUnchanged()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var model = new FakeHeadlineSummaryModel();

        var (second, _) = await RunAsync(Create(), model, Assert.Single(first.Revisions));

        Assert.Equal("unchanged", second.Entries[0].ErrorCode);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task TheDailyBudgetIsCheckedLikeStatementDraftingSoAFullDayWaits()
    {
        var world = Create();
        var store = new FakeActionDraftStore(world.Evidence()) { Charged = 9_999_999 };
        var model = new FakeHeadlineSummaryModel();

        await AnalysisWorkers.ActionDrafting(store, new FakeAnalysisStatusStore(), world.Reader(), model,
            new ActionDraftSettings(10_000_000, concurrency: 1), BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);

        Assert.Equal((State.WaitingForBudget, "dailyTokenLimit"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task TheRevisionFollowsTheCurrentRevisionNumberAndNeverTheModelsChoice()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var (second, _) = await RunAsync(Create(title: "A renamed housing bill"), new FakeHeadlineSummaryModel().Draft(Output()), Assert.Single(first.Revisions));

        Assert.Equal(2, Assert.Single(second.Revisions).RevisionNumber);
    }

    [Fact]
    public async Task TheSummaryCitationCoversOnlyTheNewestSummaryBlockOnItsOwnPage()
    {
        var world = Create(summaries:
        [
            new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>First.</p>"),
            new("01", "2025-03-01", "2025-03-02T10:00:00Z", "<p>Newest.</p>"),
            new("02", "2025-02-01", "2025-02-02T10:00:00Z", "<p>Third.</p>")
        ]);

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

        var citation = Assert.Single(store.Revisions).SummaryCitation!;
        Assert.Equal(ExtractionIdOf("summaries-1"), citation.ExtractionId);
        Assert.Equal("Summary version: 01\nAction date: 2025-03-01\nAction: Introduced\nUpdated: 2025-03-02T10:00:00Z\nText:\nText of <p>Newest.</p>",
            world.Quote(citation));
    }

    [Fact]
    public async Task TheNewestSummaryOnTheSecondPageIsCitedOnThatPage()
    {
        var world = Create(summaries:
        [
            new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>First.</p>"),
            new("01", "2025-03-01", "2025-03-02T10:00:00Z", "<p>Newest.</p>")
        ]);

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.Equal(ExtractionIdOf("summaries-1"), Assert.Single(store.Revisions).SummaryCitation!.ExtractionId);
    }

    [Theory]
    [InlineData("detail")]
    [InlineData("cosponsors-0")]
    [InlineData("summaries-0")]
    public async Task ASavedExtractionFromAnOlderParserOrExtractorBlocksWithItsOwnCodeAndNoCall(string jobId)
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]], summaries: [Summary]);
        world.SetVersion(jobId, "older-version");
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Blocked, "extractionVersionChanged"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Theory]
    [InlineData("detail", "detail")]
    [InlineData("cosponsors-0", "cosponsors-0")]
    [InlineData("summaries-0", "summaries-0")]
    [InlineData("summaries-0", "detail")]
    public async Task AChangedVersionBlocksBeforeAnyCaptureIsParsedSoACaptureTheCurrentParserRejectsStillReportsTheUpgrade(string changedJob,
        string unreadableJob)
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]], summaries: [Summary]);
        world.SetVersion(changedJob, "older-version");
        world.ReplaceCapturedBody(unreadableJob, "not json");
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Blocked, "extractionVersionChanged"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ATamperedTextWithCurrentVersionsStaysARenderedTextMismatch()
    {
        var world = Create(summaries: [Summary]);
        world.TamperWithSavedText("detail", "Bill: 119 hr 1\nTitle: Changed");

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel());

        Assert.Equal("renderedTextMismatch", store.Entries[0].ErrorCode);
    }

    [Fact]
    public async Task AWithdrawnThenRejoinedCosponsorKeepsBothEntriesEachCitingItsOwnExactLine()
    {
        var world = Create(sponsors: [Stranger], cosponsorPages: [[new(Markey, "2025-01-04", Original: true, Withdrawn: "2025-02-10"), new(Markey, "2025-02-20")]]);

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(UnnamedOutput));

        var entries = Assert.Single(store.Revisions).Sponsorships;
        Assert.Equal(2, entries.Length);
        Assert.Equal(["Cosponsor: M000133, 2025-02-20", "Cosponsor: M000133, original, 2025-01-04, withdrawn 2025-02-10"],
            entries.Select(entry => world.Quote(Assert.Single(entry.Citations))).Order());
        Assert.Equal(entries.Select(entry => entry.SponsoredOn).Order(), [new DateOnly(2025, 1, 4), new DateOnly(2025, 2, 20)]);
    }

    [Fact]
    public async Task AnExactDuplicateEntryAcrossPagesCountsOnceSoTheFingerprintMatchesASinglePage()
    {
        var once = Create(sponsors: [Stranger], cosponsorPages: [[MarkeyOriginal]]);
        var twice = Create(sponsors: [Stranger], cosponsorPages: [[MarkeyOriginal], [MarkeyOriginal]]);

        var (single, _) = await RunAsync(once, new FakeHeadlineSummaryModel().Draft(UnnamedOutput));
        var (doubled, _) = await RunAsync(twice, new FakeHeadlineSummaryModel().Draft(UnnamedOutput));

        Assert.Single(Assert.Single(doubled.Revisions).Sponsorships);
        Assert.Equal(Assert.Single(single.Revisions).Fingerprint, Assert.Single(doubled.Revisions).Fingerprint);
    }

    [Fact]
    public async Task AConfiguredPersonsLastNameAloneIsNotTheirFullNameSoItPasses()
    {
        var (store, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output(summary: "Elizabeth Warren sponsored it. Sanders is not part of it.")));

        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task AnActNamedAfterAConfiguredPersonWhoIsNotInTheFactsIsRejectedBecauseTheCheckMatchesFullNames()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: "Warren sponsored the Bernie Sanders Housing Act")).Draft(Output());

        var (store, _) = await RunAsync(Create(), model);

        Assert.Contains("who is not an official in the facts", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Single(store.Revisions);
    }

    private static async Task<(FakeActionDraftStore Store, FakeHeadlineSummaryModel Model)> RunAsync(ActionBillWorld world,
        FakeHeadlineSummaryModel model, ActionDraftRevision? current = null, DateTimeOffset? updatedAt = null, ActionDraftSettings? settings = null,
        IssueConfigurationRevision? issues = null, bool hasNewerCandidate = false)
    {
        var store = new FakeActionDraftStore(world.Evidence(current, updatedAt, hasNewerCandidate));
        await AnalysisWorkers.ActionDrafting(store, new FakeAnalysisStatusStore(), world.Reader(), model,
            settings ?? new ActionDraftSettings(10_000_000, concurrency: 1), issues ?? BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);
        return (store, model);
    }
}
