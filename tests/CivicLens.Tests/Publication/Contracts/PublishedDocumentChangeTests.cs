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
