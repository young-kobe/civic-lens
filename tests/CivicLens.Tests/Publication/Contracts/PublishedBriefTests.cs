using System.Text.Json;
using CivicLens.Core.Briefs;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedBriefTests
{
    private static readonly string BriefId = BriefWeek.RecordIdOf(new DateOnly(2026, 10, 5));

    [Fact]
    public void PublishedBriefJsonMatchesPinnedSchemaVersion()
    {
        var json = JsonSerializer.Serialize(Brief(), PublicationProtocol.JsonOptions);

        Assert.Equal(6, PublicationProtocol.SchemaVersion);
        Assert.True(json == $$"""{"recordId":"{{BriefId}}","revisionNumber":1,"headline":"A week of housing news","weekStart":"2026-10-05","cutoffReleaseNumber":3,"coverage":{"considered":5,"sentToModel":4,"cited":2},"paragraphs":[{"text":"Warren backed a rent cap.","citationIds":["R1"]},{"text":"A rule changed.","citationIds":["R1","R2"]}],"citations":[{"id":"R1","kind":"statement","recordId":"1111111111111111111111111111111111111111111111111111111111111111","revisionNumber":1,"headline":"Rent cap statement"},{"id":"R2","kind":"documentChange","recordId":"22222222222222222222222222222222","revisionNumber":3,"headline":"Zoning rule change"}],"approvedAtUtc":"1970-01-01T00:00:00+00:00","firstPublishedAtUtc":"1970-01-01T00:00:00+00:00","corrections":[]}""",
            "The published brief JSON changed. Bump PublicationProtocol.SchemaVersion, update the version table " +
            "in docs/architecture.md, then update this test. Actual: " + json);
    }

    [Fact]
    public void ValidBriefPasses()
    {
        Brief().Validate();
    }

    [Fact]
    public void TheEntryFillsOnlyTheWeekStartAsTheEventDate()
    {
        var entry = Brief().ToEntry();

        Assert.Equal((PublishedRecordKind.Brief, new DateOnly(2026, 10, 5)), (entry.Kind, entry.EventDate));
        Assert.Empty(entry.Officials);
        Assert.Empty(entry.IssueIds);
        Assert.Empty(entry.PolicyAreaIds);
        Assert.Null(entry.SourceHost);
        entry.Validate();
    }

    [Fact]
    public void RejectsAParagraphThatCitesAnIdTheBriefDoesNotListBecauseTheLinkWouldHaveNoTarget()
    {
        var brief = Brief();

        Assert.Throws<InvalidDataException>((brief with { Paragraphs = [new() { Text = "Text.", CitationIds = ["R9"] }, brief.Paragraphs[1]] }).Validate);
    }

    [Fact]
    public void RejectsAListedCitationThatNoParagraphUsesBecauseThePageWouldListARecordTheTextNeverCites()
    {
        var brief = Brief();

        Assert.Throws<InvalidDataException>((brief with { Paragraphs = [brief.Paragraphs[0]] }).Validate);
    }

    [Fact]
    public void RejectsABriefThatCitesABriefBecauseOnlyEvidenceKindsAreCitable()
    {
        var brief = Brief();
        var cited = brief.Citations[0] with { Kind = PublishedRecordKind.Brief, RecordId = BriefId };

        Assert.Throws<InvalidDataException>((brief with { Citations = [cited, brief.Citations[1]] }).Validate);
    }

    [Fact]
    public void RejectsACitedRecordIdWithTheWrongShapeForItsKind()
    {
        var brief = Brief();
        var cited = brief.Citations[0] with { RecordId = "0123456789abcdef0123456789abcdef" };

        Assert.Throws<InvalidDataException>((brief with { Citations = [cited, brief.Citations[1]] }).Validate);
    }

    [Fact]
    public void RejectsTheSameRecordCitedTwiceUnderTwoIds()
    {
        var brief = Brief();
        var again = brief.Citations[0] with { Id = "R2" };

        Assert.Throws<InvalidDataException>((brief with { Citations = [brief.Citations[0], again] }).Validate);
    }

    [Fact]
    public void RejectsCoverageThatDoesNotCountTheListedCitations()
    {
        var brief = Brief();

        Assert.Throws<InvalidDataException>((brief with { Coverage = brief.Coverage with { Cited = 1 } }).Validate);
    }

    [Fact]
    public void RejectsAWeekThatDoesNotStartOnAMonday()
    {
        Assert.Throws<InvalidDataException>((Brief() with { WeekStart = new DateOnly(2026, 10, 6) }).Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PublishedBrief.MaximumParagraphs + 1)]
    public void RequiresOneToTwelveParagraphsSoThePageStaysBounded(int count)
    {
        var brief = Brief();

        Assert.Throws<InvalidDataException>((brief with { Paragraphs = [.. Enumerable.Repeat(brief.Paragraphs[1], count)] }).Validate);
    }

    [Fact]
    public void BoundsTheHeadlineAndTheParagraphText()
    {
        var brief = Brief();

        Assert.Throws<InvalidDataException>((brief with { Headline = new string('h', PublishedBrief.MaximumHeadlineLength + 1) }).Validate);
        Assert.Throws<InvalidDataException>((brief with
        {
            Paragraphs = [brief.Paragraphs[0] with { Text = new string('p', PublishedBrief.MaximumParagraphLength + 1) }, brief.Paragraphs[1]]
        }).Validate);
    }

    [Fact]
    public void ContractLimitsEqualTheCoreLimits()
    {
        Assert.Equal(BriefRevision.MaximumHeadlineLength, PublishedBrief.MaximumHeadlineLength);
        Assert.Equal(BriefRevision.MaximumParagraphLength, PublishedBrief.MaximumParagraphLength);
        Assert.Equal(BriefRevision.MaximumParagraphs, PublishedBrief.MaximumParagraphs);
        Assert.Equal(BriefRevision.MaximumCitations, PublishedBrief.MaximumCitations);
    }

    private static PublishedBrief Brief() => new()
    {
        Corrections = [],
        RecordId = BriefId,
        RevisionNumber = 1,
        Headline = "A week of housing news",
        WeekStart = new DateOnly(2026, 10, 5),
        CutoffReleaseNumber = 3,
        Coverage = new() { Considered = 5, SentToModel = 4, Cited = 2 },
        Paragraphs =
        [
            new() { Text = "Warren backed a rent cap.", CitationIds = ["R1"] },
            new() { Text = "A rule changed.", CitationIds = ["R1", "R2"] }
        ],
        Citations =
        [
            new() { Id = "R1", Kind = PublishedRecordKind.Statement, RecordId = new string('1', 64), RevisionNumber = 1, Headline = "Rent cap statement" },
            new() { Id = "R2", Kind = PublishedRecordKind.DocumentChange, RecordId = new string('2', 32), RevisionNumber = 3, Headline = "Zoning rule change" }
        ],
        ApprovedAtUtc = DateTimeOffset.UnixEpoch,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
    };
}
