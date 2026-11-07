using System.Text.Json;
using CivicLens.Core.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Statements;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionRevisionFixtures;

namespace CivicLens.Tests.Core.Actions;

public sealed class ActionDraftRevisionTests
{
    [Fact]
    public void AnAnalysisRevisionFollowsTheCurrentOneAndAFirstRevisionIsNumberOne()
    {
        Model().ValidateAsWorkerRevision(null);
        Model(2).ValidateAsWorkerRevision(Model());

        Assert.Throws<ArgumentException>(() => Model(2).ValidateAsWorkerRevision(null));
        Assert.Throws<ArgumentException>(() => Model(3).ValidateAsWorkerRevision(Model()));
        Assert.Throws<ArgumentException>(() => Model(author: "auth0|person").ValidateAsWorkerRevision(null));
    }

    [Fact]
    public void ACodeRevisionHoldsCodeTextAndTheCodeAuthorTogetherBecauseTheLabelOnThePageNamesWhoWroteTheWords()
    {
        var code = Model() with { AuthorSubject = ReviewAuthor.CodeSubject, TextOrigin = ReviewTextOrigin.Code, BillCitation = new(ExtractionId, 0, 10) };

        code.ValidateAsWorkerRevision(null);
        Assert.Throws<ArgumentException>(() => (code with { BillCitation = null }).ValidateAsWorkerRevision(null));
        (code with { RevisionNumber = 2 }).ValidateAsWorkerRevision(code);

        Assert.Equal(ReviewAuthorKind.Code, code.AuthorKind);
        Assert.Throws<ArgumentException>(() => (code with { TextOrigin = ReviewTextOrigin.Model }).ValidateAsWorkerRevision(null));
        Assert.Throws<ArgumentException>(() => (Model() with { TextOrigin = ReviewTextOrigin.Code }).ValidateAsWorkerRevision(null));
        Assert.Throws<ArgumentException>(() => (code with { AuthorSubject = "auth0|person" }).ValidateAsWorkerRevision(null));
    }

    [Fact]
    public void ACodeRevisionNeverReplacesTextThatAPersonWroteAndNeverCarriesASuggestion()
    {
        var person = PersonEditOf(Model(), "Edited headline", "Edited summary.");
        var code = Model(3) with { AuthorSubject = ReviewAuthor.CodeSubject, TextOrigin = ReviewTextOrigin.Code, BillCitation = new(ExtractionId, 0, 10) };
        var suggestion = new ReviewTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);

        Assert.Throws<ArgumentException>(() => code.ValidateAsWorkerRevision(person with { RevisionNumber = 2 }));
        Assert.Throws<ArgumentException>(() => (code with { Suggestion = suggestion }).ValidateAsWorkerRevision(Model(2)));
    }

    [Fact]
    public void ARevisionAfterAPersonsTextMustKeepThatTextAndOfferTheModelsAsASuggestion()
    {
        var earlier = new ReviewTextSuggestion("Old", "Old summary.", new string('2', 32), Fingerprint);
        var person = PersonEditOf(Model(), "Edited headline", "Edited summary.") with { Suggestion = earlier };
        var suggestion = new ReviewTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);
        var kept = Model(3) with { Headline = person.Headline, Summary = person.Summary, TextOrigin = ReviewTextOrigin.Person, Suggestion = suggestion };

        kept.ValidateAsWorkerRevision(person);

        Assert.Throws<ArgumentException>(() => (kept with { Headline = "Overwritten" }).ValidateAsWorkerRevision(person));
        Assert.Throws<ArgumentException>(() => (kept with { Suggestion = null }).ValidateAsWorkerRevision(person));
        Assert.Throws<ArgumentException>(() => Model(3).ValidateAsWorkerRevision(person));
    }

    [Fact]
    public void ARevisionThatCopiesTheTextOriginAndSuggestionIsValidBecauseASponsorshipChangeNeedsNoNewText()
    {
        var suggestion = new ReviewTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);
        var person = PersonEditOf(Model(), "Edited headline", "Edited summary.");
        var withSuggestion = person with { Suggestion = suggestion };

        (Model(3) with { Headline = person.Headline, Summary = person.Summary, TextOrigin = ReviewTextOrigin.Person }).ValidateAsWorkerRevision(person);
        (Model(3) with { Headline = person.Headline, Summary = person.Summary, TextOrigin = ReviewTextOrigin.Person, Suggestion = suggestion })
            .ValidateAsWorkerRevision(withSuggestion);
        Model(2).ValidateAsWorkerRevision(Model());
    }

    [Fact]
    public void ModelTextAfterModelTextCarriesNoSuggestion()
    {
        var suggestion = new ReviewTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);

        Assert.Throws<ArgumentException>(() => (Model(2) with { Suggestion = suggestion }).ValidateAsWorkerRevision(Model()));
    }

    [Fact]
    public void TheChosenIssuesMustComeFromTheCandidatesBecauseTheModelMayNotInventOrPickAnUnlistedIssue()
    {
        (Model() with { TrackedIssueIds = ["rent"], IssueCandidateIds = ["rent", "eviction"] }).ValidateShape();
        (Model() with { TrackedIssueIds = [], IssueCandidateIds = ["rent"] }).ValidateShape();

        Assert.Throws<ArgumentException>(() => (Model() with { TrackedIssueIds = ["firearms"], IssueCandidateIds = ["rent"] }).ValidateShape());

        var legacy = Model() with { TrackedIssueIds = ["rent"], IssueCandidateIds = default };
        legacy.ValidateShape();
        PersonEditOf(legacy, "Edited headline", "Edited summary.").ValidatePersonEditOf(legacy);
    }

    [Fact]
    public void AnOldRevisionWithoutTheSubjectsFlagReadsAsNoSubjectsAndWritesTheSameJsonSoItsMeaningNeverChanges()
    {
        var old = Model();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var oldJson = JsonSerializer.Serialize(old, options);

        var withFlag = JsonSerializer.Serialize(old with { CandidateIncludesSubjects = true }, options);
        var back = JsonSerializer.Deserialize<ActionDraftRevision>(oldJson, options)!;

        Assert.DoesNotContain("ncludesSubjects", oldJson, StringComparison.Ordinal);
        Assert.Contains("\"candidateIncludesSubjects\":true", withFlag, StringComparison.Ordinal);
        Assert.False(back.CandidateIncludesSubjects);
        Assert.Equal(oldJson, JsonSerializer.Serialize(back, options));
        Assert.DoesNotContain("ubjectsHash", oldJson, StringComparison.Ordinal);
        Assert.Contains("\"subjectsHash\":\"" + new string('a', 64) + "\"", JsonSerializer.Serialize(old with { SubjectsHash = new string('a', 64) }, options), StringComparison.Ordinal);
        Assert.Null(back.SubjectsHash);
        Assert.Throws<ArgumentException>(() =>
            (PersonEditOf(old, "h", "s") with { CandidateIncludesSubjects = true }).ValidatePersonEditOf(old));
        Assert.Throws<ArgumentException>(() =>
            (PersonEditOf(old, "h", "s") with { SubjectsHash = new string('a', 64) }).ValidatePersonEditOf(old));
        Assert.Throws<ArgumentException>(() => (old with { SubjectsHash = "not-hex" }).ValidateShape());
    }

    [Fact]
    public void ARecordReviewerEditsOnlyTheHeadlineAndTheSummary()
    {
        var previous = Model();
        var edit = PersonEditOf(previous, "A better headline", "A better summary.");

        edit.ValidatePersonEditOf(previous);

        Assert.Throws<ArgumentException>(() => (edit with { Sponsorships = [] }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Fingerprint = new string('f', 64) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { TrackedIssueIds = ["extra"], IssueCandidateIds = ["extra"] }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { IssueCandidateIds = ["extra"] }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { PolicyArea = "Taxation", PolicyAreaCitation = new(ExtractionId, 0, 4) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Sponsorships = [Sponsorship() with { WithdrawnOn = new DateOnly(2025, 2, 1) }] }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void APersonCannotEditUnderTheAnalysisAuthorOrSkipARevision()
    {
        var previous = Model();

        Assert.Throws<ArgumentException>(() => (PersonEditOf(previous, "h", "s") with { AuthorSubject = "civic-lens:analysis" }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (PersonEditOf(previous, "h", "s") with { RevisionNumber = 3 }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (PersonEditOf(previous, "h", "s") with { TextOrigin = ReviewTextOrigin.Model }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void APersonMayKeepOrClearTheSuggestionButNeverChangeItBecauseOnlyTheModelWritesSuggestions()
    {
        var suggestion = new ReviewTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);
        var previous = Model(2) with { TextOrigin = ReviewTextOrigin.Person, Suggestion = suggestion };
        var edit = PersonEditOf(previous, "Mine", "My summary.");

        edit.ValidatePersonEditOf(previous);
        (edit with { Suggestion = null }).ValidatePersonEditOf(previous);

        Assert.Throws<ArgumentException>(() => (edit with { Suggestion = suggestion with { Headline = "Other" } }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (PersonEditOf(Model(), "h", "s") with { Suggestion = suggestion }).ValidatePersonEditOf(Model()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\0text")]
    public void TextNeedsContentAndNoNul(string headline) =>
        Assert.Throws<ArgumentException>(() => (Model() with { Headline = headline }).ValidateShape());

    [Fact]
    public void TextBoundsAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => (Model() with { Headline = new string('x', 257) }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { Summary = new string('x', 8_001) }).ValidateShape());
    }

    [Fact]
    public void ABillLineNeedsTheSummaryCitationItQuotesAndFitsOneToTwoHundredCharacters()
    {
        var cited = Model() with { SummaryVersion = "00@2025-01-05T10:00:00Z", SummaryCitation = new(ExtractionId, 0, 40), BillLine = "The bill pays for rent aid.", BillLineCitation = new(ExtractionId, 5, 25) };

        cited.ValidateShape();
        (cited with { BillLine = new string('x', ActionDraftRevision.MaximumBillLineLength) }).ValidateShape();
        Assert.Throws<ArgumentException>(() => (cited with { BillLine = new string('x', ActionDraftRevision.MaximumBillLineLength + 1) }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (cited with { BillLine = " " }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { BillLine = "The bill pays for rent aid." }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (cited with { BillLineCitation = new(ExtractionId, 40, 1) }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (cited with { BillLineCitation = new(new string('1', 64), 5, 25) }).ValidateShape());
    }

    [Fact]
    public void APersonEditKeepsTheBillLineAndItsSourceSpan()
    {
        var previous = Model() with
        {
            SummaryVersion = "00@2025-01-05T10:00:00Z",
            SummaryCitation = new(ExtractionId, 0, 40),
            BillLine = "The bill pays for rent aid.",
            BillLineCitation = new(ExtractionId, 5, 25)
        };
        var edit = previous with { RevisionNumber = previous.RevisionNumber + 1, AuthorSubject = "auth0|person", TextOrigin = ReviewTextOrigin.Person, Headline = "Updated headline" };

        edit.ValidatePersonEditOf(previous);
        Assert.Throws<ArgumentException>(() => (edit with { BillLineCitation = new(ExtractionId, 0, 25) }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void ARecordNeedsAtLeastOneSponsorshipWithBoundedCitations()
    {
        Assert.Throws<ArgumentException>(() => (Model() with { Sponsorships = [] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { Sponsorships = [Sponsorship() with { Citations = [] }] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { Sponsorships = [Sponsorship() with { Citations = Spans((0, 0)) }] }).ValidateShape());
    }

    [Fact]
    public void APersonCannotChangeTheStoredPolicyAreaID()
    {
        var previous = Model() with { PolicyArea = "Housing", PolicyAreaCitation = new(ExtractionId, 0, 5), PolicyAreaId = "housing" };
        var edit = PersonEditOf(previous, "A better headline", "A better summary.");

        edit.ValidatePersonEditOf(previous);

        Assert.Throws<ArgumentException>(() => (edit with { PolicyAreaId = "other" }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void ASponsorshipCannotBeWithdrawnBeforeItBegan()
    {
        var sponsorship = Sponsorship();
        Assert.Throws<ArgumentException>(() =>
            (Model() with { Sponsorships = [sponsorship with { WithdrawnOn = sponsorship.SponsoredOn.AddDays(-1) }] }).ValidateShape());
        (Model() with { Sponsorships = [sponsorship with { WithdrawnOn = sponsorship.SponsoredOn }] }).ValidateShape();
    }

    [Fact]
    public void APolicyAreaIDNeedsAPolicyAreaAndTextButIsOptionalBecauseTheConfigurationMayLackTheLabel()
    {
        var area = Model() with { PolicyArea = "Housing", PolicyAreaCitation = new(ExtractionId, 0, 5) };
        area.ValidateShape();
        (area with { PolicyAreaId = "housing" }).ValidateShape();
        Assert.Throws<ArgumentException>(() => (area with { PolicyAreaId = " " }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { PolicyAreaId = "housing" }).ValidateShape());
    }

    [Fact]
    public void ASummaryVersionAndAPolicyAreaEachNeedTheirCitation()
    {
        Assert.Throws<ArgumentException>(() => (Model() with { SummaryVersion = "00@2025-01-05T10:00:00Z" }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { PolicyArea = "Housing" }).ValidateShape());
        (Model() with { PolicyArea = "Housing", PolicyAreaCitation = new(ExtractionId, 0, 5) }).ValidateShape();
    }

    [Fact]
    public void ASourceIdOverTheColumnLimitFailsBecauseTheColumnCannotHoldItAndACutIdWouldNameAnotherSource()
    {
        (Model() with { SourceId = new string('s', ActionDraftRevision.MaximumSourceIdLength) }).ValidateShape();
        Assert.Throws<ArgumentException>(() =>
            (Model() with { SourceId = new string('s', ActionDraftRevision.MaximumSourceIdLength + 1) }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { SourceId = " " }).ValidateShape());
    }

    [Fact]
    public void ABillTypeOverTheColumnLimitFailsBecauseTheColumnCannotHoldItAndACutTypeWouldNameAnotherBill()
    {
        (Model() with { BillType = new string('t', ActionDraftRevision.MaximumBillTypeLength) }).ValidateShape();
        Assert.Throws<ArgumentException>(() =>
            (Model() with { BillType = new string('t', ActionDraftRevision.MaximumBillTypeLength + 1) }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { BillType = " " }).ValidateShape());
    }

    [Fact]
    public void APolicyAreaOverTheColumnLimitFailsBecauseTheColumnCannotHoldItAndACutLabelWouldStateAnotherArea()
    {
        var area = Model() with { PolicyAreaCitation = new(ExtractionId, 0, 5) };
        (area with { PolicyArea = new string('p', ActionDraftRevision.MaximumPolicyAreaLength) }).ValidateShape();
        Assert.Throws<ArgumentException>(() =>
            (area with { PolicyArea = new string('p', ActionDraftRevision.MaximumPolicyAreaLength + 1) }).ValidateShape());
    }

    [Fact]
    public void APolicyAreaIdOverTheColumnLimitFailsBecauseTheColumnCannotHoldItAndACutIdWouldNameAnotherArea()
    {
        var area = Model() with { PolicyArea = "Housing", PolicyAreaCitation = new(ExtractionId, 0, 5) };
        (area with { PolicyAreaId = new string('i', ActionDraftRevision.MaximumPolicyAreaIdLength) }).ValidateShape();
        Assert.Throws<ArgumentException>(() =>
            (area with { PolicyAreaId = new string('i', ActionDraftRevision.MaximumPolicyAreaIdLength + 1) }).ValidateShape());
    }

    [Fact]
    public void ASummaryVersionOverTheColumnLimitFailsBecauseTheColumnCannotHoldItAndACutVersionWouldNameAnotherSummary()
    {
        var versioned = Model() with { SummaryCitation = new(ExtractionId, 0, 5) };
        (versioned with { SummaryVersion = new string('v', ActionDraftRevision.MaximumSummaryVersionLength) }).ValidateShape();
        Assert.Throws<ArgumentException>(() =>
            (versioned with { SummaryVersion = new string('v', ActionDraftRevision.MaximumSummaryVersionLength + 1) }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (versioned with { SummaryVersion = " " }).ValidateShape());
    }

    [Fact]
    public void ASponsorshipPersonIdOverTheColumnLimitFailsBecauseTheColumnCannotHoldItAndACutIdWouldNameAnotherPerson()
    {
        (Model() with { Sponsorships = [Sponsorship() with { PersonId = new string('p', ActionDraftRevision.MaximumPersonIdLength) }] }).ValidateShape();
        Assert.Throws<ArgumentException>(() => (Model() with
        {
            Sponsorships = [Sponsorship() with { PersonId = new string('p', ActionDraftRevision.MaximumPersonIdLength + 1) }]
        }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { Sponsorships = [Sponsorship() with { PersonId = " " }] }).ValidateShape());
    }

    [Fact]
    public void ASponsorshipBioguideIdOverTheColumnLimitFailsBecauseTheColumnCannotHoldItAndACutIdWouldNameAnotherMember()
    {
        (Model() with { Sponsorships = [Sponsorship(new string('b', ActionDraftRevision.MaximumBioguideIdLength))] }).ValidateShape();
        Assert.Throws<ArgumentException>(() =>
            (Model() with { Sponsorships = [Sponsorship(new string('b', ActionDraftRevision.MaximumBioguideIdLength + 1))] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { Sponsorships = [Sponsorship(" ")] }).ValidateShape());
    }

    [Fact]
    public void ATrackedIssueIdOverTheColumnLimitFailsBecauseTheColumnCannotHoldItAndACutIdWouldNameAnotherIssue()
    {
        var atLimit = new string('t', ActionDraftRevision.MaximumIssueIdLength);
        (Model() with { TrackedIssueIds = [atLimit], IssueCandidateIds = [atLimit] }).ValidateShape();
        var over = atLimit + "t";
        Assert.Throws<ArgumentException>(() => (Model() with { TrackedIssueIds = [over], IssueCandidateIds = [over] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { TrackedIssueIds = [" "], IssueCandidateIds = [" "] }).ValidateShape());
    }

    [Fact]
    public void ACandidateIssueIdOverTheColumnLimitFailsBecauseTheColumnCannotHoldItAndACutIdWouldNameAnotherIssue()
    {
        var atLimit = new string('c', ActionDraftRevision.MaximumIssueIdLength);
        (Model() with { IssueCandidateIds = [atLimit] }).ValidateShape();
        Assert.Throws<ArgumentException>(() => (Model() with { IssueCandidateIds = [atLimit + "c"] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { IssueCandidateIds = [""] }).ValidateShape());
    }

    [Fact]
    public void ADuplicateIssueIdFailsBecauseTheIssueListIsASetAndARepeatWouldStoreTwoRowsForOneIssue()
    {
        (Model() with { TrackedIssueIds = ["a", "b"], IssueCandidateIds = ["a", "b"] }).ValidateShape();
        var tracked = Assert.Throws<ArgumentException>(() => (Model() with { TrackedIssueIds = ["a", "a"], IssueCandidateIds = ["a"] }).ValidateShape());
        Assert.Equal(nameof(ActionDraftRevision.TrackedIssueIds), tracked.ParamName);
        var candidates = Assert.Throws<ArgumentException>(() => (Model() with { TrackedIssueIds = ["a"], IssueCandidateIds = ["a", "a"] }).ValidateShape());
        Assert.Equal(nameof(ActionDraftRevision.IssueCandidateIds), candidates.ParamName);
    }

    [Fact]
    public void TheRevisionRoundTripsThroughJsonWithEnumsAsNamesAndNoLoss()
    {
        var revision = Model() with
        {
            TextOrigin = ReviewTextOrigin.Person,
            Suggestion = new("S", "S.", new string('1', 32), Fingerprint),
            Sponsorships = [Sponsorship() with { Role = ActionSponsorshipRole.Cosponsor, WithdrawnOn = new DateOnly(2025, 2, 1) }]
        };

        var json = JsonSerializer.Serialize(revision, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var back = JsonSerializer.Deserialize<ActionDraftRevision>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Contains("\"textOrigin\":\"Person\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"Cosponsor\"", json, StringComparison.Ordinal);
        PersonEditOf(back, revision.Headline, revision.Summary).ValidatePersonEditOf(revision);
        Assert.Equal(revision.Suggestion, back.Suggestion);
        Assert.Equal(revision.Headline, back.Headline);
        Assert.Equal(revision.Sponsorships[0], back.Sponsorships[0]);
    }

    [Fact]
    public void TheCosponsorCountSkipsWithdrawnCosponsorsAndSponsorsAndCountsEachOfficialOnceBecauseTheCountIsOfOfficialsWhoBackTheBill()
    {
        var sponsor = Sponsorship() with { PersonId = "warren" };
        ActionSponsorship Cosponsor(string person, DateOnly? withdrawn = null) =>
            sponsor with { PersonId = person, Role = ActionSponsorshipRole.Cosponsor, WithdrawnOn = withdrawn };

        var revision = Model() with
        {
            Sponsorships = [sponsor, Cosponsor("markey"), Cosponsor("markey"), Cosponsor("warren"), Cosponsor("kaine", new DateOnly(2025, 2, 1)), Cosponsor("booker")]
        };

        Assert.Equal(2, revision.CosponsorCount());
        Assert.Equal(0, (Model() with { Sponsorships = [sponsor] }).CosponsorCount());
    }

    // The review lists sort by this date, so a late withdrawal must count as the newest event of the bill.
    [Fact]
    public void TheEventDateIsTheNewestSponsoredOrWithdrawnDateAmongTheSponsorships()
    {
        var early = Sponsorship() with { PersonId = "markey", SponsoredOn = new DateOnly(2025, 1, 3), WithdrawnOn = new DateOnly(2025, 7, 1) };
        var later = Sponsorship() with { PersonId = "warren", SponsoredOn = new DateOnly(2025, 3, 1) };

        Assert.Equal(new DateOnly(2025, 7, 1), (Model() with { Sponsorships = [early, later] }).EventOn());
        Assert.Equal(new DateOnly(2025, 3, 1), (Model() with { Sponsorships = [early with { WithdrawnOn = null }, later] }).EventOn());
    }

    [Fact]
    public void TheOfficialIdsNameEachSponsorOnceEvenWhenTheySponsoredAgain()
    {
        var again = Sponsorship() with { SponsoredOn = new DateOnly(2025, 2, 1) };

        Assert.Equal(["warren"], (Model() with { Sponsorships = [Sponsorship(), again] }).OfficialIds().ToArray());
    }
}
