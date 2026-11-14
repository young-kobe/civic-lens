using CivicLens.PublicApi.Search;

namespace CivicLens.Tests.PublicApi.Search;

public sealed class SearchSnippetTests
{
    private const int Limit = SearchSnippet.MaximumCharacters;

    private static string Visible(string snippet) => snippet.Replace("", "", StringComparison.Ordinal).Replace("", "", StringComparison.Ordinal);

    [Fact]
    public void ATextWithinTheLimitStaysWholeWithItsMarks()
    {
        var text = "Tenants gain a rent cap. " + new string('w', Limit - 40);

        Assert.Equal(text.Trim(), SearchSnippet.Cut(" " + text + " "));
    }

    [Fact]
    public void ALongerTextEndsAfterItsLastWholeSentenceWithinTheLimitAndATitleBeforeANameEndsNoSentence()
    {
        var first = "The bill funds roads in each state and names Rep. Smith as its author for the record. ";
        var second = "It sets aside money for bridges, tunnels, and rail lines in rural areas of the country. ";
        var text = first + second + "It also names a crossing that runs far past the limit of the snippet and keeps going on.";

        var snippet = SearchSnippet.Cut(text);

        Assert.Equal((first + second).TrimEnd(), snippet);
        Assert.True(snippet.Length <= Limit);
    }

    [Fact]
    public void ALongSentenceIsCutAtTheLastWhiteSpaceWithinTheLimitDropsATrailingCommaAndTakesTheCutMarker()
    {
        var words = string.Concat(Enumerable.Repeat("roads, ", 60));

        var snippet = SearchSnippet.Cut("The bill funds " + words);

        Assert.StartsWith("The bill funds roads, roads", snippet, StringComparison.Ordinal);
        Assert.EndsWith("roads…", snippet, StringComparison.Ordinal);
        Assert.True(snippet.Length <= Limit);
    }

    [Fact]
    public void TheLimitCountsTheTextNotTheMarksAndACutInsideAMatchClosesItsMark()
    {
        var text = string.Concat(Enumerable.Repeat("rent ", 47)) + "rents and more words to the end";

        var snippet = SearchSnippet.Cut(text);

        Assert.True(Visible(snippet).Length <= Limit);
        Assert.True(Visible(snippet).Length > Limit - 20);
        Assert.EndsWith("…", snippet, StringComparison.Ordinal);
        Assert.Equal(snippet.Count(c => c == ''), snippet.Count(c => c == ''));
    }

    [Fact]
    public void ATextWithNoWhiteSpaceInsideTheLimitIsCutAtTheLimitWithoutSplittingACharacter()
    {
        var text = new string('x', Limit - 2) + "\U0001F600" + new string('y', 20);

        var snippet = SearchSnippet.Cut(text);

        Assert.Equal(new string('x', Limit - 2) + "…", snippet);
    }
}
