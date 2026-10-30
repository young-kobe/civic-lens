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
        Model().ValidateAsAnalysisRevision(null);
        Model(2).ValidateAsAnalysisRevision(Model());

        Assert.Throws<ArgumentException>(() => Model(2).ValidateAsAnalysisRevision(null));
        Assert.Throws<ArgumentException>(() => Model(3).ValidateAsAnalysisRevision(Model()));
        Assert.Throws<ArgumentException>(() => Model(author: "auth0|person").ValidateAsAnalysisRevision(null));
    }

    [Fact]
    public void ARevisionAfterAPersonsTextMustKeepThatTextAndOfferTheModelsAsASuggestion()
    {
        var earlier = new ReviewTextSuggestion("Old", "Old summary.", new string('2', 32), Fingerprint);
        var person = PersonEditOf(Model(), "Edited headline", "Edited summary.") with { Suggestion = earlier };
        var suggestion = new ReviewTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);
        var kept = Model(3) with { Headline = person.Headline, Summary = person.Summary, TextOrigin = ReviewTextOrigin.Person, Suggestion = suggestion };

        kept.ValidateAsAnalysisRevision(person);

        Assert.Throws<ArgumentException>(() => (kept with { Headline = "Overwritten" }).ValidateAsAnalysisRevision(person));
        Assert.Throws<ArgumentException>(() => (kept with { Suggestion = null }).ValidateAsAnalysisRevision(person));
        Assert.Throws<ArgumentException>(() => Model(3).ValidateAsAnalysisRevision(person));
    }

    [Fact]
    public void ARevisionThatCopiesTheTextOriginAndSuggestionIsValidBecauseASponsorshipChangeNeedsNoNewText()
    {
        var suggestion = new ReviewTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);
        var person = PersonEditOf(Model(), "Edited headline", "Edited summary.");
        var withSuggestion = person with { Suggestion = suggestion };

        (Model(3) with { Headline = person.Headline, Summary = person.Summary, TextOrigin = ReviewTextOrigin.Person }).ValidateAsAnalysisRevision(person);
        (Model(3) with { Headline = person.Headline, Summary = person.Summary, TextOrigin = ReviewTextOrigin.Person, Suggestion = suggestion })
            .ValidateAsAnalysisRevision(withSuggestion);
        Model(2).ValidateAsAnalysisRevision(Model());
    }

    [Fact]
    public void ModelTextAfterModelTextCarriesNoSuggestion()
    {
        var suggestion = new ReviewTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);

        Assert.Throws<ArgumentException>(() => (Model(2) with { Suggestion = suggestion }).ValidateAsAnalysisRevision(Model()));
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
