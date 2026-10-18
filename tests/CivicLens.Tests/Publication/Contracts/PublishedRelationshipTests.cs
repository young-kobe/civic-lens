using System.Text.Json;
using CivicLens.Core.Actions;
using CivicLens.Core.Relationships;
using CivicLens.Core.Statements;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedRelationshipTests
{
    private static readonly string LinkId = new('7', 64);
    private static readonly string StatementId = new('1', 64);
    private static readonly string ActionId = new('a', 64);

    [Fact]
    public void PublishedRelationshipJsonMatchesPinnedSchemaVersion()
    {
        var json = JsonSerializer.Serialize(Link(), PublicationProtocol.JsonOptions);

        Assert.Equal(3, PublicationProtocol.SchemaVersion);
        Assert.True(json == $$"""{"recordId":"{{LinkId}}","revisionNumber":1,"headline":"Doe on rent and A housing bill","statement":{"kind":"statement","recordId":"{{StatementId}}","revisionNumber":2,"headline":"Doe on rent"},"target":{"kind":"action","recordId":"{{ActionId}}","revisionNumber":3,"headline":"A housing bill"},"type":"describesOwnBill","reason":"Doe says the bill is hers.","quote":"I introduced the bill.","targetFact":"Title: A housing bill","statementDate":"2026-05-01","officials":[{"id":"o1","name":"Jane Doe"}],"issueIds":["housing"],"policyAreaIds":["land-use"],"approvedAtUtc":"1970-01-01T00:00:00+00:00","firstPublishedAtUtc":"1970-01-01T00:00:00+00:00"}""",
            "The published relationship JSON changed. Bump PublicationProtocol.SchemaVersion if a reader would break, update the version table " +
            "in docs/architecture.md, then update this test. Actual: " + json);
    }

    [Fact]
    public void ValidRelationshipPasses()
    {
        Link().Validate();
        (Link() with { Target = Link().Target with { Kind = PublishedRecordKind.Vote }, Type = PublishedRelationshipType.StatementAboutVote }).Validate();
    }

    [Fact]
    public void TheEntryTakesTheOfficialsFromTheStatementBothSidesIssuesAndTheStatementDateAndNoSourceHost()
    {
        var entry = Link().ToEntry();

        Assert.Equal((PublishedRecordKind.Relationship, new DateOnly(2026, 5, 1)), (entry.Kind, entry.EventDate));
        Assert.Equal(["o1"], entry.Officials.Select(official => official.Id));
        Assert.Equal(["housing"], entry.IssueIds);
        Assert.Equal(["land-use"], entry.PolicyAreaIds);
        Assert.Null(entry.SourceHost);
        entry.Validate();
    }

    [Fact]
    public void OtherNamesTheSideThatThePageDoesNotBelongTo()
    {
        var link = Link();

        Assert.Equal(link.Target, link.Other(PublishedRecordKind.Statement, StatementId));
        Assert.Equal(link.Statement, link.Other(PublishedRecordKind.Action, ActionId));
    }

    [Fact]
    public void RejectsATypeThatDoesNotFitTheKindOfTheTargetBecauseAVoteHasOneType()
    {
        Assert.Throws<InvalidDataException>((Link() with { Type = PublishedRelationshipType.StatementAboutVote }).Validate);
        Assert.Throws<InvalidDataException>((Link() with { Target = Link().Target with { Kind = PublishedRecordKind.Vote } }).Validate);
    }

    [Fact]
    public void RejectsASideOfTheWrongKindBecauseALinkJoinsOneStatementToOneActionOrVote()
    {
        Assert.Throws<InvalidDataException>((Link() with { Statement = Link().Statement with { Kind = PublishedRecordKind.Action } }).Validate);
        Assert.Throws<InvalidDataException>((Link() with { Target = Link().Target with { Kind = PublishedRecordKind.Statement } }).Validate);
    }

    [Fact]
    public void RejectsACitationWithoutAShapedRecordIdOrARevisionNumber()
    {
        Assert.Throws<InvalidDataException>((Link() with { Statement = Link().Statement with { RecordId = "abc" } }).Validate);
        Assert.Throws<InvalidDataException>((Link() with { Target = Link().Target with { RevisionNumber = 0 } }).Validate);
    }

    [Fact]
    public void RejectsBlankOrTooLongWordingBecauseThePageStaysBounded()
    {
        Assert.Throws<InvalidDataException>((Link() with { Reason = " " }).Validate);
        Assert.Throws<InvalidDataException>((Link() with { Reason = new string('r', PublishedRelationship.MaximumReasonLength + 1) }).Validate);
        Assert.Throws<InvalidDataException>((Link() with { Quote = "" }).Validate);
        Assert.Throws<InvalidDataException>((Link() with { TargetFact = new string('f', PublishedRelationship.MaximumFactLength + 1) }).Validate);
    }

    [Fact]
    public void RejectsRepeatedFilterValuesBecauseTheManifestEntryNeedsDistinctOnes()
    {
        Assert.Throws<InvalidDataException>((Link() with { IssueIds = ["housing", "housing"] }).Validate);
        Assert.Throws<InvalidDataException>((Link() with { PolicyAreaIds = [" "] }).Validate);
        Assert.Throws<InvalidDataException>((Link() with { Officials = [Link().Officials[0], Link().Officials[0]] }).Validate);
    }

    [Fact]
    public void ContractLimitsEqualTheCoreLimits()
    {
        Assert.Equal(RelationshipRevision.MaximumReasonLength, PublishedRelationship.MaximumReasonLength);
        Assert.Equal(StatementDraftRevision.MaximumQuotationLength, PublishedRelationship.MaximumQuoteLength);
        Assert.Equal(ActionDraftRevision.MaximumHeadlineLength, PublishedRelationship.MaximumCitedHeadlineLength);
        Assert.Equal(StatementDraftRevision.MaximumHeadlineLength, PublishedRelationship.MaximumCitedHeadlineLength);
        Assert.True(2 * PublishedRelationship.MaximumCitedHeadlineLength + " and ".Length <= PublishedRelationship.MaximumHeadlineLength);
    }

    [Fact]
    public void TheFactLimitHoldsTheLongestTargetFactThatTheReviewFormsFromAnActionSummary()
    {
        Assert.True("Summary: ".Length + ActionDraftRevision.MaximumSummaryLength <= PublishedRelationship.MaximumFactLength);
        Assert.True("Title: ".Length + ActionDraftRevision.MaximumTitleLength <= PublishedRelationship.MaximumFactLength);
    }

    [Fact]
    public void AFileWithEveryFieldAtItsLimitAndEscapedStaysUnderTheFileCapSoTheReadCapNeverRefusesAPublishedLink()
    {
        var escaped = (int length) => new string('\u0001', length);
        var citation = (PublishedRecordKind kind, string id) => new PublishedRelationshipCitation
        {
            Kind = kind,
            RecordId = id,
            RevisionNumber = 1,
            Headline = escaped(PublishedRelationship.MaximumCitedHeadlineLength)
        };
        var names = Enumerable.Range(0, PublishedRecordEntry.MaximumFilterValues).Select(index => $"{index:x2}".PadRight(PublishedRecordEntry.MaximumFilterValueLength, '\u0001')).ToArray();
        var link = Link() with
        {
            Headline = escaped(PublishedRelationship.MaximumHeadlineLength),
            Statement = citation(PublishedRecordKind.Statement, StatementId),
            Target = citation(PublishedRecordKind.Action, ActionId),
            Reason = escaped(PublishedRelationship.MaximumReasonLength),
            Quote = escaped(PublishedRelationship.MaximumQuoteLength),
            TargetFact = escaped(PublishedRelationship.MaximumFactLength),
            Officials = [.. names.Select(name => new PublishedOfficial { Id = name, Name = name })],
            IssueIds = names,
            PolicyAreaIds = names
        };

        link.Validate();
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(link, PublicationProtocol.JsonOptions).Length < PublishedRelationship.MaximumFileBytes);
    }

    private static PublishedRelationship Link() => new()
    {
        RecordId = LinkId,
        RevisionNumber = 1,
        Headline = "Doe on rent and A housing bill",
        Statement = new() { Kind = PublishedRecordKind.Statement, RecordId = StatementId, RevisionNumber = 2, Headline = "Doe on rent" },
        Target = new() { Kind = PublishedRecordKind.Action, RecordId = ActionId, RevisionNumber = 3, Headline = "A housing bill" },
        Type = PublishedRelationshipType.DescribesOwnBill,
        Reason = "Doe says the bill is hers.",
        Quote = "I introduced the bill.",
        TargetFact = "Title: A housing bill",
        StatementDate = new DateOnly(2026, 5, 1),
        Officials = [new() { Id = "o1", Name = "Jane Doe" }],
        IssueIds = ["housing"],
        PolicyAreaIds = ["land-use"],
        ApprovedAtUtc = DateTimeOffset.UnixEpoch,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
    };
}
