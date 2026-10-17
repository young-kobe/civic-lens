using CivicLens.Core.Relationships;

namespace CivicLens.Tests.Core.Relationships;

public sealed class BillTitleMatcherTests
{
    [Theory]
    [InlineData("Lower Rent Act of 2025", "Lower Rent Act")]
    [InlineData("The Lower Rent Act", "Lower Rent Act")]
    [InlineData("the lower rent act OF 2024", "lower rent act")]
    [InlineData("  Lower   Rent   Act ", "Lower Rent Act")]
    [InlineData("Lower Rent Act of 2025 and 2026", "Lower Rent Act of 2025 and 2026")]
    public void TheKeyDropsALeadingTheAndATrailingYearAndCollapsesSpaces(string title, string key) =>
        Assert.Equal(key, BillTitleMatcher.KeyOf(title));

    [Theory]
    [InlineData("Rent Act")]
    [InlineData("The Rent Act of 2025")]
    [InlineData("")]
    public void ATitleOfFewerThanThreeWordsIsTooShortToMatchBecauseItWouldMatchEverySentenceThatUsesThoseWords(string title) =>
        Assert.Null(BillTitleMatcher.KeyOf(title));

    [Fact]
    public void TheMatchIgnoresCaseAndReturnsTheWordsAsWritten()
    {
        const string text = "She wrote the LOWER RENT ACT last year.";

        var (start, length) = BillTitleMatcher.Find(text, "Lower Rent Act")!.Value;

        Assert.Equal("LOWER RENT ACT", text.Substring(start, length));
    }

    [Theory]
    [InlineData("The Lower Rent Actions plan")]
    [InlineData("The SuperLower Rent Act plan")]
    public void TheMatchNeedsWholeWords(string text) => Assert.Null(BillTitleMatcher.Find(text, "Lower Rent Act"));

    [Fact]
    public void AWholeWordMatchAfterAPartialOneIsStillFound()
    {
        const string text = "Lower Rent Actions and then the Lower Rent Act itself.";

        var (start, _) = BillTitleMatcher.Find(text, "Lower Rent Act")!.Value;

        Assert.Equal(text.LastIndexOf("Lower Rent Act", StringComparison.Ordinal), start);
    }
}
