using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionCodeTextTests
{
    private static readonly ImmutableDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["warren"] = "Elizabeth Warren",
        ["markey"] = "Edward Markey"
    }.ToImmutableDictionary();

    private static ActionBillSnapshot Snapshot(string title, string type = "hr", int summaryCount = 0, params (string Id, ActionSponsorshipRole Role)[] sponsorships)
    {
        var span = new SourceSpan(new string('a', 64), 0, 5);
        return new(new CongressBill(119, type, 3567, title, new DateOnly(2025, 5, 21), DateTimeOffset.UnixEpoch, null, [], 0, 0, summaryCount, 0, null),
            [.. sponsorships.Select(item => new ActionSponsorship(item.Id, "B000001", item.Role, false, new DateOnly(2025, 5, 21), null, [span]))],
            new string('b', 64), [], null, null, null, null, Names, [], span);
    }

    [Fact]
    public void ABillWithOneTrackedSponsorNamesTheSponsorInTheHeadlineAndTheSummary()
    {
        var text = ActionCodeText.Of(Snapshot("Put School Counselors Where They're Needed Act", "hr", 0, ("warren", ActionSponsorshipRole.Sponsor)));

        Assert.Equal("H.R. 3567: introduced by Elizabeth Warren", text.Headline);
        Assert.Equal("H.R. 3567, Put School Counselors Where They're Needed Act. Sponsor: Elizabeth Warren. Introduced 21 May 2025. " +
            "Congress.gov has no summary of this bill yet.", text.Summary);
        Assert.Equal(ReviewTextOrigin.Code, text.Origin);
    }

    [Fact]
    public void AnUntrackedSponsorWithATrackedCosponsorNamesNoOneBecauseCodeOwnsTheCosponsorList()
    {
        var text = ActionCodeText.Of(Snapshot("A bill.", "hr", 0, ("markey", ActionSponsorshipRole.Cosponsor)));

        Assert.Equal("H.R. 3567: introduced in Congress", text.Headline);
        Assert.Equal("H.R. 3567, A bill. Introduced 21 May 2025. Congress.gov has no summary of this bill yet.", text.Summary);
        Assert.DoesNotContain("Markey", text.Headline + text.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoSponsorsAreListedOnceEachWithThePluralLabel()
    {
        var text = ActionCodeText.Of(Snapshot("A bill", "s", 0, ("warren", ActionSponsorshipRole.Sponsor), ("markey", ActionSponsorshipRole.Sponsor),
            ("warren", ActionSponsorshipRole.Cosponsor)));

        Assert.Equal("S. 3567: introduced by Elizabeth Warren, Edward Markey", text.Headline);
        Assert.Contains(" Sponsors: Elizabeth Warren, Edward Markey. Introduced", text.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("An Act to fund rent aid.", "S. 3567, An Act to fund rent aid. Introduced")]
    [InlineData("  An Act to fund rent aid  ", "S. 3567, An Act to fund rent aid. Introduced")]
    [InlineData("Who pays for rent?", "S. 3567, Who pays for rent? Introduced")]
    [InlineData("The \"Rent\" Act\"", "S. 3567, The \"Rent\" Act\". Introduced")]
    [InlineData("The Rent Act (2025)", "S. 3567, The Rent Act (2025). Introduced")]
    public void TheTitleEndsWithExactlyOneEndMarkAndNeverGetsAnExtraPeriod(string title, string expected) =>
        Assert.Contains(expected, ActionCodeText.Of(Snapshot(title, "s")).Summary, StringComparison.Ordinal);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(21, false)]
    public void TheNoSummarySentenceAppearsOnlyWhenCongressGovCountsNoSummaryBecauseOtherwiseItWouldBeFalse(int summaryCount, bool sentence)
    {
        var text = ActionCodeText.Of(Snapshot("A bill", "hr", summaryCount, ("warren", ActionSponsorshipRole.Sponsor)));

        Assert.Equal(sentence, text.Summary.Contains(ActionCodeText.NoSummarySentence, StringComparison.Ordinal));
        Assert.Equal(sentence ? "H.R. 3567, A bill. Sponsor: Elizabeth Warren. Introduced 21 May 2025. Congress.gov has no summary of this bill yet."
            : "H.R. 3567, A bill. Sponsor: Elizabeth Warren. Introduced 21 May 2025.", text.Summary);
    }

    [Theory]
    [InlineData("hr", "H.R.")]
    [InlineData("s", "S.")]
    [InlineData("hjres", "H.J.Res.")]
    [InlineData("SJRES", "S.J.Res.")]
    [InlineData("hconres", "H.Con.Res.")]
    [InlineData("sconres", "S.Con.Res.")]
    [InlineData("hres", "H.Res.")]
    [InlineData("sres", "S.Res.")]
    public void EachBillTypeUsesItsPrintedAbbreviation(string type, string label) =>
        Assert.StartsWith($"{label} 3567:", ActionCodeText.Of(Snapshot("A bill", type)).Headline, StringComparison.Ordinal);
}
