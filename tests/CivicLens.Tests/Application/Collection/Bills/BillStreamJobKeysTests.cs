using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BillStreamJobKeysTests
{
    private static readonly CongressBillListItem Bill = new(119, "hr", 7, new DateOnly(2025, 3, 3));
    private static readonly DateTimeOffset UpdatedAt = new(2025, 3, 3, 12, 30, 5, TimeSpan.Zero);

    [Fact]
    public void FirstPageKeysNameTheSourceBillExactUpdateTimeAndKind()
    {
        Assert.Equal("bill-stream/congress-bills/cosponsors/119/hr/7/2025-03-03T12:30:05Z",
            BillStreamJobKeys.FirstBillPage("congress-bills", ApiResource.BillCosponsors, Bill, UpdatedAt));
        Assert.Equal("bill-stream/congress-bills/actions/119/hr/7/2025-03-03T12:30:05Z",
            BillStreamJobKeys.FirstBillPage("congress-bills", ApiResource.BillActions, Bill, UpdatedAt));
        Assert.Equal("bill-stream/congress-bills/summaries/119/hr/7/2025-03-03T12:30:05Z",
            BillStreamJobKeys.FirstBillPage("congress-bills", ApiResource.BillSummaries, Bill, UpdatedAt));
    }

    [Fact]
    public void ASecondUpdateOnTheSameDayGetsItsOwnFirstPageKeyButTheSameInstantInAnotherOffsetDoesNot()
    {
        var sameInstant = UpdatedAt.ToOffset(TimeSpan.FromHours(-5));

        Assert.NotEqual(BillStreamJobKeys.FirstBillPage("s", ApiResource.BillActions, Bill, UpdatedAt),
            BillStreamJobKeys.FirstBillPage("s", ApiResource.BillActions, Bill, UpdatedAt.AddSeconds(1)));
        Assert.Equal(BillStreamJobKeys.FirstBillPage("s", ApiResource.BillActions, Bill, UpdatedAt),
            BillStreamJobKeys.FirstBillPage("s", ApiResource.BillActions, Bill, sameInstant));
    }

    [Fact]
    public void NextPageKeysNameTheKindAndTheParentJob()
    {
        Assert.Equal("bill-stream/congress-bills/cosponsors/page/parent",
            BillStreamJobKeys.NextBillPage("congress-bills", ApiResource.BillCosponsors, "parent"));
        Assert.NotEqual(BillStreamJobKeys.NextBillPage("s", ApiResource.BillCosponsors, "parent"),
            BillStreamJobKeys.NextBillPage("s", ApiResource.BillActions, "parent"));
        Assert.NotEqual(BillStreamJobKeys.NextBillPage("s", ApiResource.BillSummaries, "parent"),
            BillStreamJobKeys.NextBillPage("s", ApiResource.BillActions, "parent"));
        Assert.NotEqual(BillStreamJobKeys.NextBillPage("s", ApiResource.BillActions, "one"),
            BillStreamJobKeys.NextBillPage("s", ApiResource.BillActions, "two"));
    }

    [Theory]
    [InlineData(ApiResource.Bill)]
    [InlineData(ApiResource.BillList)]
    public void OnlyPageKindsHaveFollowOnKeys(ApiResource resource)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BillStreamJobKeys.FirstBillPage("s", resource, Bill, UpdatedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => BillStreamJobKeys.NextBillPage("s", resource, "parent"));
    }
}
