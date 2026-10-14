using CivicLens.Application.Collection.Bills;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BillSummarySelectionTests
{
    private static CongressBillSummaryText Text(string version, string actionDate, string updated) =>
        new(new(version, DateOnly.Parse(actionDate), "Action", DateTimeOffset.Parse(updated), "<p>x</p>"), "x");

    [Fact]
    public void TheNewestSummaryWinsByActionDateThenUpdateTimeThenVersionCode()
    {
        var later = Text("00", "2025-02-01", "2025-02-01T00:00:00Z");
        var laterUpdate = Text("01", "2025-01-01", "2025-09-01T00:00:00Z");
        var higherCode = Text("05", "2025-02-01", "2025-02-01T00:00:00Z");
        var sameUpdateLowerCode = Text("02", "2025-02-01", "2025-02-01T00:00:00Z");

        Assert.Equal(later, BillSummarySelection.Newest([later, laterUpdate]));
        Assert.Equal(Text("00", "2025-02-01", "2025-03-01T00:00:00Z"),
            BillSummarySelection.Newest([later, laterUpdate, Text("00", "2025-02-01", "2025-03-01T00:00:00Z")]));
        Assert.Equal(higherCode, BillSummarySelection.Newest([sameUpdateLowerCode, higherCode, later]));
    }

    [Fact]
    public void NoSummariesGiveNoNewestAndTheVersionKeyNamesTheCodeAndTheUpdateInstantInUtc()
    {
        Assert.Null(BillSummarySelection.Newest([]));
        Assert.Equal("173@2025-01-04T15:00:00Z",
            BillSummarySelection.VersionOf(Text("173", "2025-01-03", "2025-01-04T10:00:00-05:00").Summary));
    }
}
