using CivicLens.Core.Documents;

namespace CivicLens.Tests.Core.Documents;

public sealed class DocumentContentProfileTests
{
    [Fact]
    public void ProfileSnapshotsExclusionsAndRevisionTracksEveryField()
    {
        var exclusions = new[] { ".navigation", "#footer" };
        var profile = new DocumentContentProfile("article", "main", exclusions);
        exclusions[0] = ".changed";

        Assert.Equal(new[] { ".navigation", "#footer" }, profile.ExcludedSelectors);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)profile.ExcludedSelectors)[0] = ".changed");
        Assert.Matches("^[0-9a-f]{64}$", profile.RevisionId);
        Assert.Equal("f0551066e4fcc4e270ba854f7a3f85772f16b2ea4b3d3de16114a43272da6041", profile.RevisionId);
        Assert.True(profile.Matches(new DocumentContentProfile("article", "main", [".navigation", "#footer"])));
        Assert.NotEqual(profile.RevisionId, new DocumentContentProfile("article", "main", ["#footer", ".navigation"]).RevisionId);
        Assert.NotEqual(profile.RevisionId, new DocumentContentProfile("article", "article", [".navigation", "#footer"]).RevisionId);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("#article_1")]
    [InlineData(".content-area")]
    public void AcceptsOnlySupportedSimpleSelectors(string selector) =>
        _ = new DocumentContentProfile("profile", selector, []);

    [Theory]
    [InlineData("main article")]
    [InlineData("main,article")]
    [InlineData("main:first-child")]
    [InlineData("[role=main]")]
    [InlineData("#1main")]
    [InlineData(".class name")]
    [InlineData("main\n")]
    public void RejectsSelectorSyntaxOutsideSupportedSubset(string selector) =>
        Assert.Throws<ArgumentException>(() => new DocumentContentProfile("profile", selector, []));

    [Fact]
    public void RejectsInvalidIdentityAndExclusionCollections()
    {
        Assert.Throws<ArgumentException>(() => new DocumentContentProfile("\ud800", "main", []));
        Assert.Throws<ArgumentException>(() => new DocumentContentProfile(new string('a', 129), "main", []));
        Assert.Throws<ArgumentNullException>(() => new DocumentContentProfile("profile", "main", null!));
        Assert.Throws<ArgumentException>(() => new DocumentContentProfile("profile", "main",
            default(System.Collections.Immutable.ImmutableArray<string>)));
        Assert.Throws<ArgumentException>(() => new DocumentContentProfile("profile", "main", [".one", ".one"]));
        Assert.Throws<ArgumentException>(() => new DocumentContentProfile("profile", "main", Enumerable.Range(0, 17).Select(index => $".x{index}")));
        Assert.Throws<ArgumentException>(() => new DocumentContentProfile("profile", "#" + new string('a', 129), []));
    }

    [Fact]
    public void SelectorIncludesItsPrefixInThe128CharacterLimit()
    {
        _ = new DocumentContentProfile("profile", "#" + new string('a', 127), []);
        Assert.Throws<ArgumentException>(() => new DocumentContentProfile("profile", "#" + new string('a', 128), []));
    }
}
