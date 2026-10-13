using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedDocumentChangeTests
{
    private const string BeforeId = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string AfterId = "2222222222222222222222222222222222222222222222222222222222222222";

    [Fact]
    public void AcceptsACitationThatQuotesAWholeSurrogatePair()
    {
        Record(new() { ExtractionId = AfterId, Start = 1, Length = 2, Quote = "\U0001F600" }).Validate();
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    public void RejectsACitationThatSplitsASurrogatePairBecauseTheQuoteWouldNotBeExactText(int start, int length)
    {
        var after = "a\U0001F600b";
        var citation = new PublishedCitation
        {
            ExtractionId = AfterId,
            Start = start,
            Length = length,
            Quote = after.Substring(start, length)
        };

        Assert.Throws<InvalidDataException>(() => Record(citation).Validate());
    }

    [Theory]
    [MemberData(nameof(BadLabels))]
    public void RejectsBlankOrRepeatedLabelsAndNamesTheFailingSet(PublishedDocumentChange record, string expectedMessage)
    {
        var error = Assert.Throws<InvalidDataException>(record.Validate);

        Assert.Equal(expectedMessage, error.Message);
    }

    public static TheoryData<PublishedDocumentChange, string> BadLabels()
    {
        var record = Record(new() { ExtractionId = AfterId, Start = 1, Length = 2, Quote = "\U0001F600" });
        return new()
        {
            { record with { IssueIds = null! }, "Published record officials or issues are missing." },
            { record with { Officials = [new() { Id = "mayor", Name = " " }] }, "Published record officials require a name." },
            { record with { Officials = [new() { Id = "mayor", Name = "A" }, new() { Id = "mayor", Name = "B" }] }, "Published record official ids must be distinct and not blank." },
            { record with { IssueIds = ["housing", "housing"] }, "Published record issue ids must be distinct and not blank." }
        };
    }

    [Fact]
    public void PublishedRecordJsonMatchesPinnedSchemaVersion()
    {
        var record = Record(new() { ExtractionId = AfterId, Start = 1, Length = 2, Quote = "\U0001F600" });
        var json = System.Text.Json.JsonSerializer.Serialize(record, PublicationProtocol.JsonOptions);

        Assert.Equal(2, PublicationProtocol.SchemaVersion);
        Assert.True(json == """{"recordId":"0123456789abcdef0123456789abcdef","revisionNumber":1,"headline":"Headline","summary":"Summary","significance":null,"limits":null,"institution":"Institution","changeDate":null,"changeDateEvidence":null,"officials":[],"issueIds":[],"before":{"extractionId":"1111111111111111111111111111111111111111111111111111111111111111","url":"https://example.test/a","observedAtUtc":"1970-01-01T00:00:00+00:00","text":"ab"},"after":{"extractionId":"2222222222222222222222222222222222222222222222222222222222222222","url":"https://example.test/a","observedAtUtc":"1970-01-01T00:00:00+00:00","text":"a\uD83D\uDE00b"},"changes":[{"beforeStart":1,"beforeLength":0,"afterStart":1,"afterLength":2,"beforeContextStart":0,"beforeContextLength":2,"afterContextStart":0,"afterContextLength":4,"wordEdits":[]}],"citations":[{"extractionId":"2222222222222222222222222222222222222222222222222222222222222222","start":1,"length":2,"quote":"\uD83D\uDE00"}],"approvedAtUtc":"1970-01-01T00:00:00+00:00","firstPublishedAtUtc":"1970-01-01T00:00:00+00:00"}""",
            "The published record JSON changed. Bump PublicationProtocol.SchemaVersion, update the version table " +
            "in docs/architecture.md, then update this test. Actual: " + json);
    }

    private static PublishedDocumentChange Record(PublishedCitation citation) => new()
    {
        RecordId = "0123456789abcdef0123456789abcdef",
        RevisionNumber = 1,
        Headline = "Headline",
        Summary = "Summary",
        Institution = "Institution",
        Officials = [],
        IssueIds = [],
        Before = new() { ExtractionId = BeforeId, Url = "https://example.test/a", ObservedAtUtc = DateTimeOffset.UnixEpoch, Text = "ab" },
        After = new() { ExtractionId = AfterId, Url = "https://example.test/a", ObservedAtUtc = DateTimeOffset.UnixEpoch, Text = "a\U0001F600b" },
        Changes =
        [
            new()
            {
                BeforeStart = 1, BeforeLength = 0, AfterStart = 1, AfterLength = 2,
                BeforeContextStart = 0, BeforeContextLength = 2, AfterContextStart = 0, AfterContextLength = 4,
                WordEdits = []
            }
        ],
        Citations = [citation],
        ApprovedAtUtc = DateTimeOffset.UnixEpoch,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
    };
}
