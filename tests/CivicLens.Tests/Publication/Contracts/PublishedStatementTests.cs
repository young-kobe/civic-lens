using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedStatementTests
{
    private const string SourceId = "3333333333333333333333333333333333333333333333333333333333333333";
    private const string Text = "Mayor said: \U0001F600 housing matters.";

    [Fact]
    public void PublishedStatementJsonMatchesPinnedSchemaVersion()
    {
        var json = JsonSerializer.Serialize(Statement(), PublicationProtocol.JsonOptions);

        Assert.Equal(1, PublicationProtocol.SchemaVersion);
        Assert.True(json == """{"recordId":"4444444444444444444444444444444444444444444444444444444444444444","revisionNumber":1,"headline":"Headline","summary":"Summary","statementDate":"2026-01-02","statementDateEvidence":{"extractionId":"3333333333333333333333333333333333333333333333333333333333333333","start":0,"length":5,"quote":"Mayor"},"speakers":[{"id":"mayor","name":"Mayor Example"}],"otherSpeakers":["Council Member"],"issueIds":["housing"],"policyAreaIds":["land-use"],"quotations":[{"extractionId":"3333333333333333333333333333333333333333333333333333333333333333","start":15,"length":15,"quote":"housing matters","kind":"directQuotation"}],"billNumbers":["HB 12"],"source":{"extractionId":"3333333333333333333333333333333333333333333333333333333333333333","url":"https://example.test/article","observedAtUtc":"1970-01-01T00:00:00+00:00","text":"Mayor said: \uD83D\uDE00 housing matters."},"articleUrl":null,"approvedAtUtc":"1970-01-01T00:00:00+00:00","firstPublishedAtUtc":"1970-01-01T00:00:00+00:00","corrections":[]}""",
            "The published statement JSON changed. Bump PublicationProtocol.SchemaVersion, update the version table " +
            "in docs/architecture.md, then update this test. Actual: " + json);
    }

    [Fact]
    public void ValidStatementPasses()
    {
        Statement().Validate();
    }

    [Fact]
    public void RejectsAQuotationWhoseQuoteIsNotTheTextAtItsOffsetsBecauseTheSiteWouldShowWordsNobodySaid()
    {
        var statement = Statement() with { Quotations = [Quotation(15, 15, "housing matter!")] };

        Assert.Throws<InvalidDataException>(statement.Validate);
    }

    [Theory]
    [InlineData(13, 1)]
    [InlineData(12, 1)]
    public void RejectsAQuotationThatSplitsASurrogatePair(int start, int length)
    {
        var statement = Statement() with { Quotations = [Quotation(start, length, Text.Substring(start, length))] };

        Assert.Throws<InvalidDataException>(statement.Validate);
    }

    [Fact]
    public void RejectsAQuotationOutsideTheSourceText()
    {
        var statement = Statement() with { Quotations = [Quotation(Text.Length, 1, "x")] };

        Assert.Throws<InvalidDataException>(statement.Validate);
    }

    [Fact]
    public void RejectsAQuotationOfAnotherExtraction()
    {
        var statement = Statement() with { Quotations = [Quotation(15, 15, "housing matters") with { ExtractionId = new string('9', 64) }] };

        Assert.Throws<InvalidDataException>(statement.Validate);
    }

    [Fact]
    public void RejectsAnUnknownQuotationKind()
    {
        var statement = Statement() with { Quotations = [Quotation(15, 15, "housing matters") with { Kind = (PublishedQuotationKind)7 }] };

        Assert.Throws<InvalidDataException>(statement.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PublishedStatement.MaximumQuotations + 1)]
    public void RequiresOneToThirtyTwoQuotations(int count)
    {
        var statement = Statement() with { Quotations = [.. Enumerable.Repeat(Quotation(15, 15, "housing matters"), count)] };

        Assert.Throws<InvalidDataException>(statement.Validate);
    }

    [Fact]
    public void ADateNeedsItsEvidenceAndEvidenceNeedsItsDate()
    {
        Assert.Throws<InvalidDataException>((Statement() with { StatementDateEvidence = null }).Validate);
        Assert.Throws<InvalidDataException>((Statement() with { StatementDate = null }).Validate);
    }

    [Fact]
    public void DateEvidenceMustQuoteTheSourceExactly()
    {
        var evidence = Statement().StatementDateEvidence! with { Quote = "Mayer" };

        Assert.Throws<InvalidDataException>((Statement() with { StatementDateEvidence = evidence }).Validate);
    }

    [Fact]
    public void RequiresAtLeastOneSpeakerBecauseAStatementWithoutAnOfficialIsNotTracked()
    {
        var error = Assert.Throws<InvalidDataException>((Statement() with { Speakers = [] }).Validate);

        Assert.Equal("Published statement requires at least one speaker.", error.Message);
    }

    [Theory]
    [MemberData(nameof(BadLabels))]
    public void RejectsBlankOrRepeatedLabelsAndNamesTheFailingSet(PublishedStatement statement, string expectedMessage)
    {
        var error = Assert.Throws<InvalidDataException>(statement.Validate);

        Assert.Equal(expectedMessage, error.Message);
    }

    public static TheoryData<PublishedStatement, string> BadLabels() => new()
    {
        { Statement() with { IssueIds = null! }, "Published statement speakers or labels are missing." },
        { Statement() with { Speakers = [new() { Id = "mayor", Name = " " }] }, "Published statement speakers require a name." },
        { Statement() with { Speakers = [new() { Id = "mayor", Name = "A" }, new() { Id = "mayor", Name = "B" }] }, "Published statement speaker ids must be distinct and not blank." },
        { Statement() with { OtherSpeakers = ["A", "A"] }, "Published statement other speakers must be distinct and not blank." },
        { Statement() with { IssueIds = ["housing", "housing"] }, "Published statement issue ids must be distinct and not blank." },
        { Statement() with { PolicyAreaIds = [" "] }, "Published statement policy area ids must be distinct and not blank." },
        { Statement() with { BillNumbers = [""] }, "Published statement bill numbers must be distinct and not blank." }
    };

    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void RequiresACandidateIdSoAStatementCannotUseADocumentChangeId(string recordId)
    {
        Assert.Throws<InvalidDataException>((Statement() with { RecordId = recordId }).Validate);
    }

    [Fact]
    public void RejectsASourceWithoutAWebUrl()
    {
        var source = Statement().Source with { Url = "file:///etc/passwd" };

        Assert.Throws<InvalidDataException>((Statement() with { Source = source }).Validate);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/article")]
    [InlineData("")]
    public void RejectsAnArticleLinkThatIsNotAWebUrlBecauseTheReaderFollowsIt(string link)
    {
        Assert.Throws<InvalidDataException>((Statement() with { ArticleUrl = link }).Validate);
    }

    [Fact]
    public void AcceptsAWebArticleLinkAndKeepsItThroughJson()
    {
        var statement = Statement() with { ArticleUrl = "https://senator.example/news/a" };
        statement.Validate();

        var read = JsonSerializer.Deserialize<PublishedStatement>(
            JsonSerializer.Serialize(statement, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions)!;

        Assert.Equal("https://senator.example/news/a", read.ArticleUrl);
    }

    [Fact]
    public void ARecordWrittenBeforeTheArticleLinkExistedStillReadsWithNoLink()
    {
        var json = JsonSerializer.SerializeToNode(Statement(), PublicationProtocol.JsonOptions)!.AsObject();
        json.Remove("articleUrl");

        var read = json.Deserialize<PublishedStatement>(PublicationProtocol.JsonOptions)!;

        Assert.Null(read.ArticleUrl);
        read.Validate();
    }

    internal static PublishedStatement Statement() => new()
    {
        Corrections = [],
        RecordId = new string('4', 64),
        RevisionNumber = 1,
        Headline = "Headline",
        Summary = "Summary",
        StatementDate = new DateOnly(2026, 1, 2),
        StatementDateEvidence = new() { ExtractionId = SourceId, Start = 0, Length = 5, Quote = "Mayor" },
        Speakers = [new() { Id = "mayor", Name = "Mayor Example" }],
        OtherSpeakers = ["Council Member"],
        IssueIds = ["housing"],
        PolicyAreaIds = ["land-use"],
        Quotations = [Quotation(15, 15, "housing matters")],
        BillNumbers = ["HB 12"],
        Source = new() { ExtractionId = SourceId, Url = "https://example.test/article", ObservedAtUtc = DateTimeOffset.UnixEpoch, Text = Text },
        ApprovedAtUtc = DateTimeOffset.UnixEpoch,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
    };

    private static PublishedQuotation Quotation(int start, int length, string quote) => new()
    {
        ExtractionId = SourceId,
        Start = start,
        Length = length,
        Quote = quote,
        Kind = PublishedQuotationKind.DirectQuotation
    };
}
