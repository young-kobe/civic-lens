using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PulledReportTests
{
    private static PulledReport Report(string category = "wrong-quote", string text = "") => new()
    {
        ReportId = Guid.NewGuid(),
        RecordId = new string('a', 64),
        RevisionNumber = 1,
        Kind = PublishedRecordKind.Statement,
        ReleaseNumber = 1,
        Category = category,
        Text = text,
        ReceivedAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public void AValidReportAndABatchOfItPassTheValidation()
    {
        new PulledReportBatch { Reports = [Report()], NextCursor = "c" }.Validate();
    }

    [Theory]
    [InlineData("rude", "")]
    [InlineData("other", "")]
    [InlineData("wrong-quote", "a\0b")]
    public void AReportWithABadCategoryOrTextIsRefusedBecauseThePullerTrustsNothingTheServiceSends(string category, string text)
    {
        Assert.Throws<InvalidDataException>(() => Report(category, text).Validate());
    }

    [Fact]
    public void ABatchWithoutACursorOrWithTooManyReportsIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => new PulledReportBatch { Reports = [], NextCursor = "" }.Validate());
        Assert.Throws<InvalidDataException>(() =>
            new PulledReportBatch { Reports = [.. Enumerable.Repeat(Report(), ReaderReportProtocol.MaximumBatchSize + 1)], NextCursor = "c" }.Validate());
    }
}
