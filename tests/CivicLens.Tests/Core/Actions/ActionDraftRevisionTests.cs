using System.Text.Json;
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
        var person = PersonEditOf(Model(), "Edited headline", "Edited summary.");
        var suggestion = new ActionTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);
        var kept = Model(3) with { Headline = person.Headline, Summary = person.Summary, TextOrigin = ActionTextOrigin.Person, Suggestion = suggestion };

        kept.ValidateAsAnalysisRevision(person);

        Assert.Throws<ArgumentException>(() => (kept with { Headline = "Overwritten" }).ValidateAsAnalysisRevision(person));
        Assert.Throws<ArgumentException>(() => (kept with { Suggestion = null }).ValidateAsAnalysisRevision(person));
        Assert.Throws<ArgumentException>(() => Model(3).ValidateAsAnalysisRevision(person));
    }

    [Fact]
    public void ModelTextAfterModelTextCarriesNoSuggestion()
    {
        var suggestion = new ActionTextSuggestion("New", "New summary.", new string('1', 32), Fingerprint);

        Assert.Throws<ArgumentException>(() => (Model(2) with { Suggestion = suggestion }).ValidateAsAnalysisRevision(Model()));
    }

    [Fact]
    public void ARecordReviewerEditsOnlyTheHeadlineAndTheSummary()
    {
        var previous = Model();
        var edit = PersonEditOf(previous, "A better headline", "A better summary.");

        edit.ValidatePersonEditOf(previous);

        Assert.Throws<ArgumentException>(() => (edit with { Sponsorships = [] }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Fingerprint = new string('f', 64) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { TrackedIssueIds = ["extra"] }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { PolicyArea = "Taxation", PolicyAreaCitation = new(ExtractionId, 0, 4) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Sponsorships = [Sponsorship() with { WithdrawnOn = new DateOnly(2025, 2, 1) }] }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void APersonCannotEditUnderTheAnalysisAuthorOrSkipARevision()
    {
        var previous = Model();

        Assert.Throws<ArgumentException>(() => (PersonEditOf(previous, "h", "s") with { AuthorSubject = "civic-lens:analysis" }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (PersonEditOf(previous, "h", "s") with { RevisionNumber = 3 }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (PersonEditOf(previous, "h", "s") with { TextOrigin = ActionTextOrigin.Model }).ValidatePersonEditOf(previous));
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
            TextOrigin = ActionTextOrigin.Person,
            Suggestion = new("S", "S.", new string('1', 32), Fingerprint),
            Sponsorships = [Sponsorship() with { Role = ActionSponsorshipRole.Cosponsor, WithdrawnOn = new DateOnly(2025, 2, 1) }]
        };

        var json = JsonSerializer.Serialize(revision, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var back = JsonSerializer.Deserialize<ActionDraftRevision>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Contains("\"textOrigin\":\"Person\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"Cosponsor\"", json, StringComparison.Ordinal);
        Assert.True(revision.HasSameStructuredPartAs(back));
        Assert.Equal(revision.Headline, back.Headline);
        Assert.Equal(revision.Sponsorships[0], back.Sponsorships[0]);
    }
}
