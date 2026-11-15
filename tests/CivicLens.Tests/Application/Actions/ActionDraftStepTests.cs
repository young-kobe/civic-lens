using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Issues;
using CivicLens.Core.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Core.Statements;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillWorld;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionDraftStepTests
{
    /// <summary>The answer of the issue-only call, the one call that a bill with candidate issues gets unless the bill text drafts.</summary>
    private static string Output(string[]? issueIds = null) => IssueOutput(issueIds ?? ["rent"]);

    private static readonly CosponsorSpec MarkeyOriginal = new(Markey, "2025-01-04", Original: true);
    private static readonly SummarySpec Summary = new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>");

    private static ActionBillWorld Bare(string? policyArea = BillGateFixtures.TrackedPolicyArea, CosponsorSpec[][]? cosponsorPages = null,
        string title = "A housing bill") =>
        ActionBillWorld.Create(cosponsorPages: cosponsorPages, summaries: [], policyArea: policyArea, title: title);

    private static string IssueOutput(params string[] ids) =>
        JsonSerializer.Serialize(new { issueIds = ids, confidence = 0.8 });

    // A model drafts only from summary text, so a bill in these tests holds a summary unless a test passes none.
    private static ActionBillWorld Create(string[]? sponsors = null, CosponsorSpec[][]? cosponsorPages = null, SummarySpec[]? summaries = null,
        string? policyArea = BillGateFixtures.TrackedPolicyArea, DateTimeOffset? updatedAt = null, string title = "A housing bill", int extraPeople = 0,
        string warrenPersonId = "warren", string[][]? subjectPages = null) =>
        ActionBillWorld.Create(sponsors, cosponsorPages, summaries ?? [Summary], policyArea, updatedAt, title, extraPeople, warrenPersonId, subjectPages);

    [Fact]
    public async Task ACrsBillGetsCodeTextAndOneIssueOnlyCallSoNoModelWritesTextFromTheCrsSummary()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]], summaries: [Summary]);
        var (store, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((1, "civic-lens:code", ReviewTextOrigin.Code, null), (revision.RevisionNumber, revision.AuthorSubject, revision.TextOrigin, revision.Suggestion));
        Assert.Equal(("H.R. 1: A housing bill", "H.R. 1, A housing bill. Sponsor: Elizabeth Warren. Introduced 3 Jan 2025."), (revision.Headline, revision.Summary));
        Assert.Equal(("congress-bills", 119, "hr", 1), (revision.SourceId, revision.Congress, revision.BillType, revision.BillNumber));
        Assert.Equal(new DateTimeOffset(2025, 3, 3, 12, 0, 0, TimeSpan.Zero), revision.CandidateUpdateDate);
        Assert.Matches("^[0-9a-f]{64}$", revision.Fingerprint);
        Assert.Equal("00@2025-01-05T10:00:00Z", revision.SummaryVersion);
        Assert.Contains("It funds rent aid.", world.Quote(revision.SummaryCitation!), StringComparison.Ordinal);
        Assert.Equal(["rent"], revision.TrackedIssueIds.ToArray());
        Assert.Equal(["rent"], revision.IssueCandidateIds.ToArray());
        Assert.Equal("housing", revision.PolicyAreaId);
        Assert.Equal(BillGateFixtures.Revision().Id, revision.IssueRevisionId);
        var request = Assert.Single(model.Requests);
        Assert.Equal((ActionDraftTask.IssuePromptVersion, ActionDraftTask.IssueSchemaVersion), (request.PromptVersion, request.SchemaVersion));
        Assert.DoesNotContain("rent aid", request.UserPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("headline", request.SchemaJson, StringComparison.Ordinal);
        var entry = Assert.Single(store.Entries);
        Assert.Equal((State.Succeeded, null), (entry.Status, entry.ErrorCode));
    }

    [Fact]
    public async Task ACrsBillWithNoCandidateIssueGetsCodeTextWithNoModelCallAndNoCharge()
    {
        var model = new FakeHeadlineSummaryModel();
        var (store, _) = await RunAsync(Create(policyArea: BillGateFixtures.UntrackedPolicyArea), model);

        Assert.Empty(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((ReviewTextOrigin.Code, "00@2025-01-05T10:00:00Z"), (revision.TextOrigin, revision.SummaryVersion));
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, 0L, ActionDraftErrorCodes.CodeWritten), (run.Run.Outcome, run.Run.ChargedTokens, run.Reason));
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
        var (store, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(JsonSerializer.Serialize(new { issueIds = new[] { "rent" }, confidence = 0.77 })));

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
        var (store, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

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
        Assert.Equal((2, current.Headline, current.Summary, ReviewTextOrigin.Code, null),
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

        var (first, _) = await RunAsync(active, new FakeHeadlineSummaryModel().Draft(Output()));
        var (second, model) = await RunAsync(withdrawn, new FakeHeadlineSummaryModel(), Assert.Single(first.Revisions), updatedAt: UpdatedAt.AddDays(1));

        Assert.Equal(new DateOnly(2025, 2, 10), Assert.Single(Assert.Single(second.Revisions).Sponsorships).WithdrawnOn);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ANewSponsorWritesNewCodeTextBecauseTheTextNamesTheSponsors()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var (second, _) = await RunAsync(Create(sponsors: [Warren, Markey]), model, Assert.Single(first.Revisions), updatedAt: UpdatedAt.AddDays(1));

        Assert.Single(model.Requests);
        Assert.Contains("Sponsors: ", Assert.Single(second.Revisions).Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASponsorshipChangeTogetherWithAnotherFactStillCallsTheModel()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var (second, _) = await RunAsync(Create(cosponsorPages: [[MarkeyOriginal]], title: "A renamed housing bill"), model,
            Assert.Single(first.Revisions), updatedAt: UpdatedAt.AddDays(1));

        Assert.Single(model.Requests);
        Assert.Equal("H.R. 1: A renamed housing bill", Assert.Single(second.Revisions).Headline);
    }

    [Fact]
    public async Task ANewerSummaryDraftsAgainEvenWhenNoSponsorshipChanged()
    {
        var older = Create(summaries: [Summary]);
        var newer = Create(summaries: [Summary, new SummarySpec("01", "2025-02-01", "2025-02-02T10:00:00Z", "<p>Amended.</p>")]);
        var (first, _) = await RunAsync(older, new FakeHeadlineSummaryModel().Draft(Output()));

        var (second, model) = await RunAsync(newer, new FakeHeadlineSummaryModel().Draft(Output()), Assert.Single(first.Revisions));

        var revision = Assert.Single(second.Revisions);
        Assert.Equal("01@2025-02-02T10:00:00Z", revision.SummaryVersion);
        Assert.Contains("Amended.", newer.Quote(revision.SummaryCitation!), StringComparison.Ordinal);
        Assert.DoesNotContain("Amended.", Assert.Single(model.Requests).UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABillWithNoSummaryAndNoCandidateIssueGetsCodeTextWithNoModelCallAndNoChargeBecauseTheModelAddsNothing()
    {
        var (store, model) = await RunAsync(Bare(BillGateFixtures.UntrackedPolicyArea), new FakeHeadlineSummaryModel());

        Assert.Empty(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal(("civic-lens:code", ReviewTextOrigin.Code, null), (revision.AuthorSubject, revision.TextOrigin, revision.Suggestion));
        Assert.Equal("H.R. 1: A housing bill", revision.Headline);
        Assert.Equal("H.R. 1, A housing bill. Sponsor: Elizabeth Warren. Introduced 3 Jan 2025. Congress.gov has no summary of this bill yet.",
            revision.Summary);
        Assert.Empty(revision.TrackedIssueIds);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, 0L, ActionDraftErrorCodes.CodeWritten), (run.Run.Outcome, run.Run.ChargedTokens, run.Reason));
        var entry = Assert.Single(store.Entries);
        Assert.Equal((State.Succeeded, null), (entry.Status, entry.ErrorCode));
    }

    [Fact]
    public async Task ABillWithCandidateIssuesButNoSummaryTextMakesOneIssueOnlyCallAndKeepsTheCodeTextBecauseTheModelHasNoWordsToWrite()
    {
        var (store, model) = await RunAsync(Bare(), new FakeHeadlineSummaryModel().Draft(IssueOutput("rent")));

        var request = Assert.Single(model.Requests);
        Assert.StartsWith("You choose which candidate issues", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal((ActionDraftTask.IssuePromptVersion, ActionDraftTask.IssueSchemaVersion), (request.PromptVersion, request.SchemaVersion));
        Assert.DoesNotContain("headline", request.SchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"summary\"", request.UserPrompt, StringComparison.Ordinal);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal(("civic-lens:code", ReviewTextOrigin.Code), (revision.AuthorSubject, revision.TextOrigin));
        Assert.Equal(["rent"], revision.TrackedIssueIds.ToArray());
        Assert.Contains("Congress.gov has no summary of this bill yet.", revision.Summary, StringComparison.Ordinal);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Drafted, ActionDraftTask.IssuePromptVersion), (run.Run.Outcome, run.Run.PromptVersion));
        Assert.True(run.Run.ChargedTokens > 0);
        Assert.Equal((State.Succeeded, null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task AnEmptyIssuePickIsAValidAnswerAndLeavesTheCodeRecordWithoutIssues()
    {
        var (store, _) = await RunAsync(Bare(), new FakeHeadlineSummaryModel().Draft(IssueOutput()));

        Assert.Empty(Assert.Single(store.Revisions).TrackedIssueIds);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData(null)]
    public async Task AWrongOrMissingIssuePickFailsThroughTheValidationLoopAndTheRetryCarriesTheReason(string? wrong)
    {
        var bad = wrong is null ? "{\"confidence\":0.5}" : IssueOutput(wrong);
        var model = new FakeHeadlineSummaryModel().Draft(bad).Draft(IssueOutput("rent"));

        var (store, _) = await RunAsync(Bare(), model);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains(wrong is null ? "issueIds is required" : "which is not a candidate issue", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
        Assert.Equal(["rent"], Assert.Single(store.Revisions).TrackedIssueIds.ToArray());
    }

    [Fact]
    public async Task TwoWrongIssuePicksFailTheEntryAndWriteNoRevision()
    {
        var model = new FakeHeadlineSummaryModel().Draft(IssueOutput("nope")).Draft(IssueOutput("nope"));

        var (store, _) = await RunAsync(Bare(), model);

        Assert.Equal((State.Failed, "outputRejected"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task ASummaryThatArrivesOnACodeWrittenRecordWritesNewCodeTextThatCitesItWithNoModelCall()
    {
        var untracked = BillGateFixtures.UntrackedPolicyArea;
        var (first, _) = await RunAsync(Bare(untracked), new FakeHeadlineSummaryModel());
        var codeWritten = Assert.Single(first.Revisions);

        var (second, model) = await RunAsync(Create(policyArea: untracked), new FakeHeadlineSummaryModel(), codeWritten);

        Assert.Empty(model.Requests);
        var revision = Assert.Single(second.Revisions);
        Assert.Equal(("civic-lens:code", ReviewTextOrigin.Code, 2, "00@2025-01-05T10:00:00Z"),
            (revision.AuthorSubject, revision.TextOrigin, revision.RevisionNumber, revision.SummaryVersion));
        Assert.DoesNotContain(ActionCodeText.NoSummarySentence, revision.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACodeWrittenRecordIsUnchangedOnTheNextRunAndKeepsItsCodeTextWhenOnlyACosponsorJoins()
    {
        var untracked = BillGateFixtures.UntrackedPolicyArea;
        var (first, _) = await RunAsync(Bare(untracked), new FakeHeadlineSummaryModel());
        var codeWritten = Assert.Single(first.Revisions);

        var (again, _) = await RunAsync(Bare(untracked), new FakeHeadlineSummaryModel(), codeWritten);
        var (joined, model) = await RunAsync(Bare(untracked, cosponsorPages: [[MarkeyOriginal]]), new FakeHeadlineSummaryModel(), codeWritten);

        Assert.Empty(again.Revisions);
        Assert.Empty(model.Requests);
        var copy = Assert.Single(joined.Revisions);
        Assert.Equal(("civic-lens:code", ReviewTextOrigin.Code, codeWritten.Summary), (copy.AuthorSubject, copy.TextOrigin, copy.Summary));
        Assert.Equal(2, copy.Sponsorships.Length);
    }

    [Fact]
    public async Task TextThatAPersonWroteStaysAndGetsNoSuggestionCallWhenNoSummaryTextCanReachTheModel()
    {
        var untracked = BillGateFixtures.UntrackedPolicyArea;
        var (first, _) = await RunAsync(Bare(untracked), new FakeHeadlineSummaryModel());
        var edited = Assert.Single(first.Revisions) with
        {
            RevisionNumber = 2,
            AuthorSubject = "auth0|editor",
            TextOrigin = ReviewTextOrigin.Person,
            Headline = "Edited"
        };

        var (second, model) = await RunAsync(Bare(untracked, title: "A renamed bill"), new FakeHeadlineSummaryModel(), edited);

        Assert.Empty(model.Requests);
        var revision = Assert.Single(second.Revisions);
        Assert.Equal((ReviewTextOrigin.Person, "Edited", null, "A renamed bill"), (revision.TextOrigin, revision.Headline, revision.Suggestion, revision.BillTitle));
        Assert.Equal(ActionDraftErrorCodes.TextKept, Assert.Single(second.Runs).Reason);
    }

    [Fact]
    public async Task TextThatAPersonWroteStaysWithNoSuggestionCallEvenWhenSummaryTextExists()
    {
        var untracked = BillGateFixtures.UntrackedPolicyArea;
        var (first, _) = await RunAsync(Bare(untracked), new FakeHeadlineSummaryModel());
        var edited = Assert.Single(first.Revisions) with { RevisionNumber = 2, AuthorSubject = "auth0|editor", TextOrigin = ReviewTextOrigin.Person, Headline = "Edited" };

        var (second, model) = await RunAsync(Create(), new FakeHeadlineSummaryModel(), edited);

        Assert.Empty(model.Requests);
        var revision = Assert.Single(second.Revisions);
        Assert.Equal((ReviewTextOrigin.Person, "Edited", null), (revision.TextOrigin, revision.Headline, revision.Suggestion));
        Assert.Equal(ActionDraftErrorCodes.TextKept, Assert.Single(second.Runs).Reason);
    }

    [Fact]
    public async Task AStaleCrsModelDraftBecomesCodeTextWithOneIssueCallWhenARedraftReachesIt()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]]);
        var (first, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));
        var current = RetiredCrsText(Assert.Single(first.Revisions));

        var (unchanged, unchangedModel) = await RunAsync(world, new FakeHeadlineSummaryModel(), current);
        var (redrafted, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()), current, hasStaleDraft: true);

        Assert.Empty(unchanged.Revisions);
        Assert.Empty(unchangedModel.Requests);
        Assert.Equal(ActionDraftTask.IssuePromptVersion, Assert.Single(model.Requests).PromptVersion);
        var revision = Assert.Single(redrafted.Revisions);
        Assert.Equal((ReviewTextOrigin.Code, "H.R. 1: A housing bill", current.SummaryVersion), (revision.TextOrigin, revision.Headline, revision.SummaryVersion));
    }

    [Fact]
    public async Task ARedraftOfAnOldFullDraftOnABillWithNoSummaryBecomesCodeTextWithAtMostOneIssueCall()
    {
        var (seed, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var oldDraft = RetiredCrsText(Assert.Single(seed.Revisions)) with
        {
            SummaryVersion = null,
            SummaryCitation = null,
            Summary = "Warren introduced a housing bill. Congress.gov has no summary of this bill yet."
        };

        var (withIssues, issueModel) = await RunAsync(Bare(), new FakeHeadlineSummaryModel().Draft(IssueOutput("rent")), oldDraft, hasStaleDraft: true);
        var (noIssues, plainModel) = await RunAsync(Bare(BillGateFixtures.UntrackedPolicyArea), new FakeHeadlineSummaryModel(), oldDraft with
        {
            PolicyArea = BillGateFixtures.UntrackedPolicyArea,
            PolicyAreaId = null,
            TrackedIssueIds = [],
            IssueCandidateIds = []
        }, hasStaleDraft: true);

        Assert.Single(issueModel.Requests);
        var converted = Assert.Single(withIssues.Revisions);
        Assert.Equal((ReviewTextOrigin.Code, "civic-lens:code"), (converted.TextOrigin, converted.AuthorSubject));
        Assert.Equal(["rent"], converted.TrackedIssueIds.ToArray());
        Assert.Empty(plainModel.Requests);
        Assert.Equal(ReviewTextOrigin.Code, Assert.Single(noIssues.Revisions).TextOrigin);
    }

    [Fact]
    public async Task ModelTextFromASummaryStaysWhenANewerCandidateHoldsNoSummaryTextAndNothingIsPaidTwice()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var drafted = RetiredCrsText(Assert.Single(first.Revisions));
        Func<ActionDraftEvidence, ActionDraftEvidence> withoutSummaryPages = evidence =>
            evidence with { Pages = [.. evidence.Pages.Where(page => page.Resource != ApiResource.BillSummaries)] };

        var (kept, keptModel) = await RunAsync(Create(title: "A renamed bill"), new FakeHeadlineSummaryModel(), drafted, shape: withoutSummaryPages);
        var copy = Assert.Single(kept.Revisions);
        var (again, againModel) = await RunAsync(Create(title: "A renamed bill"), new FakeHeadlineSummaryModel(), copy, shape: withoutSummaryPages);
        var (stale, staleModel) = await RunAsync(Create(title: "A renamed bill"), new FakeHeadlineSummaryModel(), copy, hasStaleDraft: true,
            shape: withoutSummaryPages);

        Assert.Empty(keptModel.Requests);
        Assert.Equal((drafted.Headline, drafted.Summary, ReviewTextOrigin.Model, drafted.SummaryVersion, "A renamed bill"),
            (copy.Headline, copy.Summary, copy.TextOrigin, copy.SummaryVersion, copy.BillTitle));
        var run = Assert.Single(kept.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, 0L, ActionDraftErrorCodes.TextKept), (run.Run.Outcome, run.Run.ChargedTokens, run.Reason));
        Assert.Empty(again.Revisions);
        Assert.Empty(againModel.Requests);
        Assert.Empty(stale.Revisions);
        Assert.Empty(staleModel.Requests);
        Assert.Equal(ActionDraftErrorCodes.Unchanged, stale.Entries[0].ErrorCode);
    }

    [Fact]
    public async Task ModelTextGivesWayToCodeTextOnlyWhenCongressGovCountsNoSummaryAnymore()
    {
        var untracked = BillGateFixtures.UntrackedPolicyArea;
        var (first, _) = await RunAsync(Create(policyArea: untracked), new FakeHeadlineSummaryModel());

        var (second, model) = await RunAsync(Bare(untracked), new FakeHeadlineSummaryModel(), RetiredCrsText(Assert.Single(first.Revisions)));

        Assert.Empty(model.Requests);
        var revision = Assert.Single(second.Revisions);
        Assert.Equal((ReviewTextOrigin.Code, 2, null), (revision.TextOrigin, revision.RevisionNumber, revision.SummaryVersion));
    }

    [Fact]
    public async Task ACodeRecordLosesTheNoSummarySentenceWithoutAModelCallAsSoonAsCongressGovCountsASummary()
    {
        var untracked = BillGateFixtures.UntrackedPolicyArea;
        var (first, _) = await RunAsync(Bare(untracked), new FakeHeadlineSummaryModel());

        var (second, model) = await RunAsync(Create(policyArea: untracked), new FakeHeadlineSummaryModel(), Assert.Single(first.Revisions),
            shape: evidence => evidence with { Pages = [.. evidence.Pages.Where(page => page.Resource != ApiResource.BillSummaries)] });

        Assert.Empty(model.Requests);
        var revision = Assert.Single(second.Revisions);
        Assert.Equal((ReviewTextOrigin.Code, 2), (revision.TextOrigin, revision.RevisionNumber));
        Assert.Equal("H.R. 1, A housing bill. Sponsor: Elizabeth Warren. Introduced 3 Jan 2025.", revision.Summary);
    }

    [Fact]
    public async Task ACodeRecordGetsOneIssueCallWhenCandidateIssuesAppearAndStaysCodeText()
    {
        var (first, _) = await RunAsync(Bare(BillGateFixtures.UntrackedPolicyArea), new FakeHeadlineSummaryModel());

        var (second, model) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()), Assert.Single(first.Revisions));

        Assert.Equal(ActionDraftTask.IssuePromptVersion, Assert.Single(model.Requests).PromptVersion);
        var revision = Assert.Single(second.Revisions);
        Assert.Equal(ReviewTextOrigin.Code, revision.TextOrigin);
        Assert.Equal(["rent"], revision.TrackedIssueIds.ToArray());
    }

    [Fact]
    public async Task ACodeRecordCitesTheBillPageEvenWhenNoSponsorAndNoPolicyAreaCiteItBecauseTheTitleAndTheDateComeFromThatPage()
    {
        var world = ActionBillWorld.Create(sponsors: [Stranger], cosponsorPages: [[MarkeyOriginal]], summaries: [], policyArea: null);

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel());

        var revision = Assert.Single(store.Revisions);
        Assert.Equal("H.R. 1: A housing bill", revision.Headline);
        Assert.DoesNotContain("Sponsor", revision.Summary, StringComparison.Ordinal);
        Assert.Null(revision.PolicyAreaCitation);
        var cited = Assert.Single(revision.Citations, span => span == revision.BillCitation);
        var page = world.Evidence().Pages.Single(item => item.Resource == ApiResource.Bill);
        Assert.Equal(page.ExtractionId, cited.ExtractionId);
        Assert.StartsWith("Title: A housing bill\nIntroduced: ", page.Text.Substring(cited.Start, cited.Length), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStaleBillWithOnlyACosponsorChangeStillCopiesTheTextAtNoChargeBecauseTheCopyNeedsNoPrompt()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));

        var (second, model) = await RunAsync(Create(cosponsorPages: [[MarkeyOriginal]]), new FakeHeadlineSummaryModel(), Assert.Single(first.Revisions),
            hasStaleDraft: true);

        Assert.Empty(model.Requests);
        var run = Assert.Single(second.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, 0L, ActionDraftErrorCodes.SponsorshipsChanged), (run.Run.Outcome, run.Run.ChargedTokens, run.Reason));
    }

    [Fact]
    public async Task MoreThanTwentySummaryPagesNeverReachTheModelSoCodeWritesTheTextWithoutTheNoSummarySentence()
    {
        var summaries = Enumerable.Range(0, 21).Select(index => new SummarySpec($"{index:00}", "2025-01-03", "2025-01-05T10:00:00Z", "<p>pagebody</p>")).ToArray();
        var world = Create(summaries: summaries);
        for (var index = 0; index < summaries.Length; index++) world.ReplaceCapturedBody($"summaries-{index}", "not json");
        world.SetVersion("summaries-0", "older-version");
        var model = new FakeHeadlineSummaryModel().Draft(IssueOutput("rent"));

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Succeeded, (string?)null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((ReviewTextOrigin.Code, null, null), (revision.TextOrigin, revision.SummaryVersion, revision.SummaryCitation));
        Assert.DoesNotContain("has no summary", revision.Summary, StringComparison.Ordinal);
        Assert.True(revision.CandidateIncludesSummaries);
        Assert.DoesNotContain("pagebody", Assert.Single(model.Requests).UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALongSummaryNeverReachesTheModelSoCodeWritesTheTextAndOneIssueCallChoosesTheIssues()
    {
        var big = Create(summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>" + new string('x', 150_000) + "</p>")]);
        var model = new FakeHeadlineSummaryModel().Draft(IssueOutput("rent"));

        var (store, _) = await RunAsync(big, model);

        var request = Assert.Single(model.Requests);
        Assert.DoesNotContain("xxxxxxxxxx", request.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(ActionDraftTask.IssuePromptVersion, request.PromptVersion);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((ReviewTextOrigin.Code, "00@2025-01-05T10:00:00Z"), (revision.TextOrigin, revision.SummaryVersion));
        Assert.DoesNotContain("has no summary", revision.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FactsThatAloneExceedTheByteCapStillBlockWithoutACall()
    {
        var model = new FakeHeadlineSummaryModel();
        var settings = new ActionDraftSettings(ActionIssuePrompt.MinimumReservation, 1);

        var (store, _) = await RunAsync(Create(title: new string('t', 3_900)), model, settings: settings);

        Assert.Equal((State.Blocked, "overByteCap"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ACandidateWithSubjectsDraftsAgainFromARevisionThatNeverSawThemAndALaterRunOfTheSameCandidateIsUnchanged()
    {
        var withoutSubjects = Create(summaries: [Summary]);
        var withSubjects = Create(summaries: [Summary], subjectPages: [["Housing finance and home ownership"], ["Rent and rental housing"]]);
        var (first, _) = await RunAsync(withoutSubjects, new FakeHeadlineSummaryModel().Draft(Output()));
        var before = Assert.Single(first.Revisions);

        var (second, model) = await RunAsync(withSubjects, new FakeHeadlineSummaryModel().Draft(Output()), before);
        var (third, thirdModel) = await RunAsync(withSubjects, new FakeHeadlineSummaryModel(), Assert.Single(second.Revisions));

        Assert.False(before.CandidateIncludesSubjects);
        var revised = Assert.Single(second.Revisions);
        Assert.True(revised.CandidateIncludesSubjects);
        var prompt = Assert.Single(model.Requests).UserPrompt;
        Assert.Contains("\"Housing finance and home ownership\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"Rent and rental housing\"", prompt, StringComparison.Ordinal);
        Assert.Empty(thirdModel.Requests);
        Assert.Equal((State.Succeeded, "unchanged"), (third.Entries[0].Status, third.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ACandidateWhosePromptWouldBeByteIdenticalDoesNotDraftAgainButRealSubjectsLaterDoAndAChangedSubjectDoesToo()
    {
        var plain = Create(summaries: [Summary]);
        var policyOnly = Create(summaries: [Summary], subjectPages: [[]]);
        var real = Create(summaries: [Summary], subjectPages: [["Housing finance and home ownership"]]);
        var changed = Create(summaries: [Summary], subjectPages: [["Rent and rental housing"]]);
        var (first, _) = await RunAsync(plain, new FakeHeadlineSummaryModel().Draft(Output()));
        var before = Assert.Single(first.Revisions);

        var (second, policyOnlyModel) = await RunAsync(policyOnly, new FakeHeadlineSummaryModel(), before);
        var (third, realModel) = await RunAsync(real, new FakeHeadlineSummaryModel().Draft(Output()), before);
        var withSubjects = Assert.Single(third.Revisions);
        var (fourth, changedModel) = await RunAsync(changed, new FakeHeadlineSummaryModel().Draft(Output()), withSubjects);

        Assert.Empty(policyOnlyModel.Requests);
        Assert.Equal((State.Succeeded, "unchanged"), (second.Entries[0].Status, second.Entries[0].ErrorCode));
        Assert.Single(realModel.Requests);
        Assert.Matches("^[0-9a-f]{64}$", withSubjects.SubjectsHash);
        Assert.Single(changedModel.Requests);
        Assert.NotEqual(withSubjects.SubjectsHash, Assert.Single(fourth.Revisions).SubjectsHash);
    }

    [Fact]
    public async Task ACopiedRevisionFromACandidateWithoutSubjectsKeepsTheHashSoTheLaterCandidateWithTheSameSubjectsIsUnchanged()
    {
        var subjects = new[] { new[] { "Rent and rental housing" } };
        var first = Create(summaries: [Summary], subjectPages: subjects);
        var withoutSubjects = Create(cosponsorPages: [[MarkeyOriginal]], summaries: [Summary]);
        var later = Create(cosponsorPages: [[MarkeyOriginal]], summaries: [Summary], subjectPages: subjects);
        var (drafted, _) = await RunAsync(first, new FakeHeadlineSummaryModel().Draft(Output()));
        var withHash = Assert.Single(drafted.Revisions);

        var (copied, copyModel) = await RunAsync(withoutSubjects, new FakeHeadlineSummaryModel(), withHash);
        var copy = Assert.Single(copied.Revisions);
        var (settled, laterModel) = await RunAsync(later, new FakeHeadlineSummaryModel(), copy);

        Assert.NotNull(withHash.SubjectsHash);
        Assert.Empty(copyModel.Requests);
        Assert.Equal(withHash.SubjectsHash, copy.SubjectsHash);
        Assert.Empty(laterModel.Requests);
        Assert.Equal((State.Succeeded, "unchanged"), (settled.Entries[0].Status, settled.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ABillWithNoSubjectsDraftsWithoutThemAndTheRevisionKeepsTheOldShape()
    {
        var (store, model) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.False(Assert.Single(store.Revisions).CandidateIncludesSubjects);
        Assert.DoesNotContain("subjects", Assert.Single(model.Requests).UserPrompt, StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(Assert.Single(store.Revisions));
        Assert.DoesNotContain("CandidateIncludesSubjects", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SubjectsHash", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASubjectsPageThatNoLongerMatchesItsCaptureBlocksBeforeAnyCall(bool tamperText)
    {
        var world = Create(subjectPages: [["Rent and rental housing"]]);
        if (tamperText) world.TamperWithSavedText("subjects-0", "Bill: 119 hr 1\nSubject: Changed after capture");
        else world.SetVersion("subjects-0", "older-version");
        var model = new FakeHeadlineSummaryModel();

        var (store, _) = await RunAsync(world, model);

        Assert.Equal((State.Blocked, tamperText ? "renderedTextMismatch" : "extractionVersionChanged"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task OnlyTheNewestSummaryIsCitedByActionDateThenUpdateTimeThenVersionCode()
    {
        var world = Create(summaries:
        [
            new("02", "2025-02-01", "2025-02-02T10:00:00Z", "<p>Second.</p>"),
            new("03", "2025-02-01", "2025-02-02T10:00:00Z", "<p>Third.</p>"),
            new("09", "2025-01-01", "2025-09-02T10:00:00Z", "<p>Old action.</p>")
        ]);

        var (store, model) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

        var cited = world.Quote(Assert.Single(store.Revisions).SummaryCitation!);
        Assert.Contains("Third.", cited, StringComparison.Ordinal);
        Assert.DoesNotContain("Second.", cited, StringComparison.Ordinal);
        Assert.DoesNotContain("Old action.", cited, StringComparison.Ordinal);
        Assert.DoesNotContain("Third.", Assert.Single(model.Requests).UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheModelGetsNoLatestActionAndNoOtherFactsThanTheAgreedOnes()
    {
        var (_, model) = await RunAsync(Create(summaries: [Summary]), new FakeHeadlineSummaryModel().Draft(Output()));

        using var document = JsonDocument.Parse(model.Requests[0].UserPrompt["Facts:\n".Length..]);
        Assert.Equal(["bill", "candidateIssues", "sponsors"], document.RootElement.EnumerateObject().Select(item => item.Name));
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
    public async Task TwentySummaryPagesAreStillRead()
    {
        var summaries = Enumerable.Range(0, 20).Select(index => new SummarySpec($"{index:00}", "2025-01-03", "2025-01-05T10:00:00Z", "<p>x</p>")).ToArray();

        var (store, _) = await RunAsync(Create(summaries: summaries), new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task APersonsTextIsKeptWithNoCallAndNoSuggestionOnABillWithSummaryText()
    {
        var world = Create(cosponsorPages: [[MarkeyOriginal]], title: "A renamed housing bill");
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var edited = ActionRevisionFixtures.PersonEditOf(Assert.Single(first.Revisions), "Edited headline", "Edited summary.");
        var model = new FakeHeadlineSummaryModel();

        var (second, _) = await RunAsync(world, model, edited);

        Assert.Empty(model.Requests);
        var revision = Assert.Single(second.Revisions);
        Assert.Equal(("Edited headline", "Edited summary.", ReviewTextOrigin.Person, null), (revision.Headline, revision.Summary, revision.TextOrigin, revision.Suggestion));
        Assert.Equal(["M000133", "W000817"], revision.Sponsorships.Select(item => item.BioguideId).Order());
    }

    [Fact]
    public async Task ALaterRunStillKeepsThePersonsTextBecauseTheTextOriginTravelsWithTheRevision()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var edited = ActionRevisionFixtures.PersonEditOf(Assert.Single(first.Revisions), "Edited headline", "Edited summary.");
        var (second, _) = await RunAsync(Create(title: "One title"), new FakeHeadlineSummaryModel(), edited);
        var keptByAnalysis = Assert.Single(second.Revisions);
        Assert.Equal("civic-lens:analysis", keptByAnalysis.AuthorSubject);

        var third = Create(title: "Two title");
        var (last, _) = await RunAsync(third, new FakeHeadlineSummaryModel(), keptByAnalysis);

        var revision = Assert.Single(last.Revisions);
        Assert.Equal(("Edited headline", "Edited summary.", 4), (revision.Headline, revision.Summary, revision.RevisionNumber));
        Assert.Null(revision.Suggestion);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public async Task AnIssueConfidenceOutsideZeroToOneIsRejected(double confidence)
    {
        var answer = JsonSerializer.Serialize(new { issueIds = new[] { "rent" }, confidence });
        var model = new FakeHeadlineSummaryModel().Draft(answer).Draft(answer);

        var (store, _) = await RunAsync(Create(), model);

        Assert.Equal((State.Failed, "outputRejected"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Contains("must be a number from 0 to 1", model.Requests[1].UserPrompt, StringComparison.Ordinal);
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

        var (store, _) = await RunAsync(world, new FakeHeadlineSummaryModel().Draft(Output()));

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

        var (single, _) = await RunAsync(once, new FakeHeadlineSummaryModel().Draft(Output()));
        var (doubled, _) = await RunAsync(twice, new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.Single(Assert.Single(doubled.Revisions).Sponsorships);
        Assert.Equal(Assert.Single(single.Revisions).Fingerprint, Assert.Single(doubled.Revisions).Fingerprint);
    }

    [Fact]
    public async Task ARejectedRecordSettlesAsUnchangedWithoutACallEvenWhenTheBillChangedBecauseTheTokensWouldBeWasted()
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var drafted = Assert.Single(first.Revisions);

        var (rejected, model) = await RunAsync(Create(title: "A renamed bill"), new FakeHeadlineSummaryModel(), drafted,
            shape: evidence => evidence with { IsRejected = true });

        Assert.Empty(model.Requests);
        Assert.Empty(rejected.Revisions);
        Assert.Equal((State.Succeeded, ActionDraftErrorCodes.Unchanged), (rejected.Entries[0].Status, rejected.Entries[0].ErrorCode));
        Assert.Equal(ActionDraftErrorCodes.RecordRejected, Assert.Single(rejected.Runs).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARejectedRecordWithTheSameFactsLeavesNoBlockEvenWhenThePromptIsOlderBecauseNothingWasRefused(bool olderPrompt)
    {
        var (first, _) = await RunAsync(Create(), new FakeHeadlineSummaryModel().Draft(Output()));
        var drafted = Assert.Single(first.Revisions);

        var (rejected, model) = await RunAsync(Create(), new FakeHeadlineSummaryModel(), drafted, hasStaleDraft: olderPrompt,
            shape: evidence => evidence with { IsRejected = true });

        Assert.Empty(model.Requests);
        Assert.Equal(ActionDraftErrorCodes.Unchanged, Assert.Single(rejected.Runs).Reason);
    }

    /// <summary>A revision as the retired CRS prompt drafted it: model text with the cited summary, which production still holds.</summary>
    private static ActionDraftRevision RetiredCrsText(ActionDraftRevision codeRevision) => codeRevision with
    {
        AuthorSubject = "civic-lens:analysis",
        TextOrigin = ReviewTextOrigin.Model,
        Headline = "Warren sponsored a housing bill",
        Summary = "Elizabeth Warren introduced a housing bill on January 3, 2025.",
        BillCitation = null
    };

    private static async Task<(FakeActionDraftStore Store, FakeHeadlineSummaryModel Model)> RunAsync(ActionBillWorld world,
        FakeHeadlineSummaryModel model, ActionDraftRevision? current = null, DateTimeOffset? updatedAt = null, ActionDraftSettings? settings = null,
        IssueConfigurationRevision? issues = null, bool hasNewerCandidate = false, bool hasStaleDraft = false,
        Func<ActionDraftEvidence, ActionDraftEvidence>? shape = null)
    {
        var evidence = world.Evidence(current, updatedAt, hasNewerCandidate, hasStaleDraft);
        var store = new FakeActionDraftStore(shape is null ? evidence : shape(evidence));
        await AnalysisWorkers.ActionDrafting(store, new FakeAnalysisStatusStore(), world.Reader(), model,
            settings ?? new ActionDraftSettings(10_000_000, concurrency: 1), issues ?? BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);
        return (store, model);
    }
}
