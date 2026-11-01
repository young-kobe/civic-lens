using System.Globalization;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillRendererTests
{
    private static readonly CongressBillListItem Item = new(119, "hr", 1234, default);

    private static CongressBill Bill(string title = "A bill", string? policyArea = "Immigration", string[]? sponsors = null,
        DateTimeOffset? updated = null) =>
        new(119, "hr", 1234, title, new DateOnly(2026, 3, 2), updated ?? new DateTimeOffset(2026, 10, 6, 14, 3, 0, TimeSpan.Zero),
            policyArea, sponsors ?? ["L000595"], 12, 71, 3, 0, null);

    [Fact]
    public void DetailIsOneFactPerLineInAFixedOrderSoEachFactIsACleanCitationSpan()
    {
        const string expected = "Bill: 119 hr 1234\nTitle: A bill\nIntroduced: 2026-03-02\nUpdated: 2026-10-06T14:03:00Z\n" +
            "Policy area: Immigration\nSponsor: L000595\nCosponsors (including withdrawn): 12\nActions: 71";

        Assert.Equal(expected, CongressBillRenderer.RenderDetail(Bill()));
    }

    [Fact]
    public void DetailOmitsAMissingPolicyAreaAndListsEverySponsorOnItsOwnLine()
    {
        const string expected = "Bill: 119 hr 1234\nTitle: A bill\nIntroduced: 2026-03-02\nUpdated: 2026-10-06T14:03:00Z\n" +
            "Sponsor: L000595\nSponsor: S001234\nCosponsors (including withdrawn): 12\nActions: 71";

        Assert.Equal(expected, CongressBillRenderer.RenderDetail(Bill(policyArea: null, sponsors: ["L000595", "S001234"])));
    }

    [Fact]
    public void DetailWritesTheUpdateTimeInUtcSoTheSameInstantNeverRendersTwoWays()
    {
        var local = new DateTimeOffset(2026, 10, 6, 10, 3, 0, TimeSpan.FromHours(-4));

        Assert.Equal(CongressBillRenderer.RenderDetail(Bill()), CongressBillRenderer.RenderDetail(Bill(updated: local)));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\v")]
    [InlineData("\f")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void EachLineBreakCharacterBecomesOneSpace(string lineBreak)
    {
        var page = new CongressBillActionsPage(
            [new CongressBillAction(new DateOnly(2026, 10, 1), null, "one" + lineBreak + "two", null, null, null, null)], null);

        Assert.Equal("Bill: 119 hr 1234\nAction 2026-10-01: one two", CongressBillRenderer.RenderActions(Item, page));
    }

    [Fact]
    public void ATaggedValueWithALineBreakStaysOnOneLine()
    {
        var page = new CongressBillActionsPage(
            [new CongressBillAction(new DateOnly(2026, 10, 1), null, "Text.", "a\nb", "c\u2028d", 1, "e\vf")], null);

        Assert.Equal("Bill: 119 hr 1234\nAction 2026-10-01 [type a b] [code c d] [source 1 e f]: Text.",
            CongressBillRenderer.RenderActions(Item, page));
    }

    [Fact]
    public void ATitleWithLineBreaksStaysOneLineSoOneFactStaysOneLine()
    {
        var text = CongressBillRenderer.RenderDetail(Bill(title: "  First\r\nsecond\rthird\nfourth  "));

        Assert.Contains("\nTitle: First second third fourth\n", text);
    }

    [Fact]
    public void CosponsorsRenderOriginalAndWithdrawnStateInFixedFieldOrder()
    {
        var page = new CongressBillCosponsorsPage(
        [
            new CongressBillCosponsor("S001234", new DateOnly(2026, 3, 2), true, null),
            new CongressBillCosponsor("S001234", new DateOnly(2026, 4, 1), false, new DateOnly(2026, 5, 1)),
            new CongressBillCosponsor("L000595", new DateOnly(2026, 4, 2), false, null),
            new CongressBillCosponsor("A000375", new DateOnly(2026, 3, 2), true, new DateOnly(2026, 6, 1))
        ], "https://api.congress.gov/next");
        const string expected = "Bill: 119 hr 1234\nCosponsor: S001234, original, 2026-03-02\n" +
            "Cosponsor: S001234, 2026-04-01, withdrawn 2026-05-01\nCosponsor: L000595, 2026-04-02\n" +
            "Cosponsor: A000375, original, 2026-03-02, withdrawn 2026-06-01";

        Assert.Equal(expected, CongressBillRenderer.RenderCosponsors(Item, page));
    }

    [Fact]
    public void ActionsRenderEveryParsedFieldAsATaggedPartInAFixedOrderAndOmitNullOnes()
    {
        var page = new CongressBillActionsPage(
        [
            new CongressBillAction(new DateOnly(2026, 10, 1), new TimeOnly(14, 3, 0), "Referred to the Committee on Rules.",
                "IntroReferral", "H11100", 2, "House floor actions"),
            new CongressBillAction(new DateOnly(2026, 10, 2), null, "Passed House.", "Floor", null, null, null),
            new CongressBillAction(new DateOnly(2026, 10, 3), new TimeOnly(9, 5, 7), "Held at the desk.", null, "H12345", 7, null),
            new CongressBillAction(new DateOnly(2026, 10, 4), null, "Read twice.", null, null, null, "Senate"),
            new CongressBillAction(new DateOnly(2026, 10, 5), null, "Introduced.", null, null, null, null)
        ], null);
        const string expected = "Bill: 119 hr 1234\n" +
            "Action 2026-10-01 14:03:00 [type IntroReferral] [code H11100] [source 2 House floor actions]: Referred to the Committee on Rules.\n" +
            "Action 2026-10-02 [type Floor]: Passed House.\n" +
            "Action 2026-10-03 09:05:07 [code H12345] [source 7]: Held at the desk.\n" +
            "Action 2026-10-04 [source Senate]: Read twice.\n" +
            "Action 2026-10-05: Introduced.";

        Assert.Equal(expected, CongressBillRenderer.RenderActions(Item, page));
    }

    [Fact]
    public void ABracketOrParenthesisInATaggedValueIsWrittenAsReceivedBecauseTheLineStaysOneFact()
    {
        var page = new CongressBillActionsPage(
            [new CongressBillAction(new DateOnly(2026, 10, 1), null, "Text.", "A) b] c", null, null, null)], null);

        Assert.Equal("Bill: 119 hr 1234\nAction 2026-10-01 [type A) b] c]: Text.", CongressBillRenderer.RenderActions(Item, page));
    }

    [Fact]
    public void ActionTextKeepsItsWordsAndSpacingExceptThatEachLineBreakBecomesOneSpace()
    {
        var page = new CongressBillActionsPage(
            [new CongressBillAction(new DateOnly(2026, 10, 1), null, "Motion  to reconsider\r\nlaid on the table.\nAgreed.\r", null, null, null, null)],
            null);

        var lines = CongressBillRenderer.RenderActions(Item, page).Split('\n');

        Assert.Equal(["Bill: 119 hr 1234", "Action 2026-10-01: Motion  to reconsider laid on the table. Agreed."], lines);
    }

    [Fact]
    public void SummariesRenderOneBlockEachWithLabeledFactsAndAMultiLineTextAfterTheTextLabel()
    {
        var summaries = new[]
        {
            new CongressBillSummaryText(new CongressBillSummary("173", new DateOnly(2022, 2, 18), "Passed Senate",
                new DateTimeOffset(2022, 2, 18, 11, 38, 41, TimeSpan.FromHours(-5)), "<p>x</p>"), "First line\nSecond line"),
            new CongressBillSummaryText(new CongressBillSummary("00", new DateOnly(2022, 3, 1), "Introduced\nin Senate",
                new DateTimeOffset(2022, 3, 1, 10, 0, 0, TimeSpan.Zero), ""), "")
        };
        const string expected = "Bill: 119 hr 1234\n\n" +
            "Summary version: 173\nAction date: 2022-02-18\nAction: Passed Senate\nUpdated: 2022-02-18T16:38:41Z\nText:\nFirst line\nSecond line\n\n" +
            "Summary version: 00\nAction date: 2022-03-01\nAction: Introduced in Senate\nUpdated: 2022-03-01T10:00:00Z\nText:";

        Assert.Equal(expected, CongressBillRenderer.RenderSummaries(Item, summaries));
    }

    [Theory]
    [InlineData("one\n\ntwo")]
    [InlineData("one\r\ntwo")]
    [InlineData("one\rtwo")]
    public void SummaryTextWithABlankLineOrCarriageReturnIsInvalidBecauseABlankLineIsTheOnlyBlockSeparator(string text)
    {
        var summary = new CongressBillSummary("1", new DateOnly(2022, 1, 1), "a", DateTimeOffset.UnixEpoch, "");

        Assert.Throws<InvalidDataException>(() =>
            CongressBillRenderer.RenderSummaries(Item, [new CongressBillSummaryText(summary, text)]));
    }

    [Fact]
    public void ASubjectsPageIsOneSubjectLinePerLineAfterTheBillLine()
    {
        var page = new CongressBillSubjectsPage(["Housing finance", "Rent\nand\r\nleases"], null);

        Assert.Equal("Bill: 119 hr 1234\nSubject: Housing finance\nSubject: Rent and leases", CongressBillRenderer.RenderSubjects(Item, page));
    }

    [Fact]
    public void AnEmptyPageStillNamesItsBillAndEndsWithoutWhitespace()
    {
        Assert.Equal("Bill: 119 hr 1234", CongressBillRenderer.RenderCosponsors(Item, new CongressBillCosponsorsPage([], null)));
        Assert.Equal("Bill: 119 hr 1234", CongressBillRenderer.RenderActions(Item, new CongressBillActionsPage([], null)));
        Assert.Equal("Bill: 119 hr 1234", CongressBillRenderer.RenderSummaries(Item, []));
        Assert.Equal("Bill: 119 hr 1234", CongressBillRenderer.RenderSubjects(Item, new CongressBillSubjectsPage([], null)));
    }

    [Fact]
    public void TheSameInputAlwaysGivesTheSameText()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var other = CongressBillRenderer.RenderDetail(Bill());
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Equal(CongressBillRenderer.RenderDetail(Bill()), other);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
