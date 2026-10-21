using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PulledReportTests
{
    public static PulledReport Report(Guid? id = null) => new()
    {
        ReportId = id ?? Guid.NewGuid(),
        RecordId = new string('a', 64),
        RevisionNumber = 1,
        Kind = PublishedRecordKind.Statement,
        ReleaseNumber = 1,
        Category = ReaderReportProtocol.WrongQuoteCategory,
        Text = "The quote is cut short.",
        ReceivedAtUtc = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)
    };

    public static PulledReportBatch Batch(params PulledReport[] reports) => new() { Reports = reports, NextCursor = "epoch.1" };

    [Fact]
    public void AReportThatTheServiceMayStoreIsValidIncludingAnEmptyTextForACategoryOtherThanOther()
    {
        Report().Validate();
        (Report() with { Text = "" }).Validate();
        (Report() with { RecordId = new string('b', 32), Kind = PublishedRecordKind.Brief }).Validate();
    }

    [Theory]
    [InlineData("recordId")]
    [InlineData("revision")]
    [InlineData("category")]
    [InlineData("otherWithoutText")]
    [InlineData("nul")]
    [InlineData("longText")]
    [InlineData("kind")]
    [InlineData("emptyId")]
    public void AReportThatBreaksAWireRuleIsRefusedBecauseTheServiceIsAnOutsideBoundary(string rule)
    {
        var broken = rule switch
        {
            "recordId" => Report() with { RecordId = "not-a-record" },
            "revision" => Report() with { RevisionNumber = 0 },
            "category" => Report() with { Category = "spam" },
            "otherWithoutText" => Report() with { Category = ReaderReportProtocol.OtherCategory, Text = " " },
            "nul" => Report() with { Text = "a\0b" },
            "longText" => Report() with { Text = new string('x', ReaderReportProtocol.MaximumTextCharacters + 1) },
            "kind" => Report() with { Kind = (PublishedRecordKind)99 },
            _ => Report() with { ReportId = Guid.Empty }
        };

        Assert.Throws<InvalidDataException>(broken.Validate);
    }

    [Fact]
    public void ABatchWithOneBadReportADuplicateIdOrABadCursorIsRefusedAsAWhole()
    {
        var id = Guid.NewGuid();

        Assert.Throws<InvalidDataException>(() => Batch(Report(), Report() with { RevisionNumber = 0 }).Validate());
        Assert.Throws<InvalidDataException>(() => Batch(Report(id), Report(id)).Validate());
        Assert.Throws<InvalidDataException>(() => (Batch(Report()) with { NextCursor = "" }).Validate());
        Assert.Throws<InvalidDataException>(() =>
            (Batch(Report()) with { NextCursor = new string('x', ReaderReportProtocol.MaximumCursorLength + 1) }).Validate());
        Batch(Report(), Report()).Validate();
    }
}
