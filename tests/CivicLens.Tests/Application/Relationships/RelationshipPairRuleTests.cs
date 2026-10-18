using CivicLens.Application.Relationships;

namespace CivicLens.Tests.Application.Relationships;

public sealed class RelationshipPairRuleTests
{
    [Fact]
    public void ABillNumberMatchNeedsTheSameCongressTypeAndNumberSoAMentionWithoutACongressNeverMatches()
    {
        Assert.True(RelationshipPairRule.IsSameBill(119, "hr", 1234, 119, "hr", 1234));
        Assert.False(RelationshipPairRule.IsSameBill(118, "hr", 1234, 119, "hr", 1234));
        Assert.False(RelationshipPairRule.IsSameBill(119, "s", 1234, 119, "hr", 1234));
        Assert.False(RelationshipPairRule.IsSameBill(119, "hr", 1235, 119, "hr", 1234));
        Assert.False(RelationshipPairRule.IsSameBill(null, "hr", 1234, 119, "hr", 1234));
        Assert.False(RelationshipPairRule.IsSameBill(119, "hr", 1234, 119, null, null));
    }

    [Fact]
    public void CitedBillsNormalizeTheCitationTypeAndListEachBillOnce()
    {
        var cited = RelationshipPairRule.CitedBills("H.R. 1234 and H.Con.Res. 14 and again H.R. 1234.");

        Assert.Equal([("hr", 1234), ("hconres", 14)], cited.ToArray());
    }
}
