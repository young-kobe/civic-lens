using CivicLens.PublicApi.Pages;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.PublicApi.Pages;

public sealed class BuildIdTests
{
    [Fact]
    public void TheBuildIdIsTheGitShaAfterThePlusOfTheInformationalVersion()
    {
        var sha = new string('a', 40);

        Assert.Equal(sha, BuildId.FromAssembly(TestCodeVersion.AssemblyVersioned($"1.0.0+{sha}")));
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+bad sha\"")]
    [InlineData("1.0.0+abcdef0")]
    [InlineData("1.0.0+gggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("1.0.0+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("1.0.0+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1.0.0+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ABuildWithoutExactlyFortyLowercaseHexDigitsStopsServerPageStartup(string informationalVersion)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => BuildId.FromAssembly(TestCodeVersion.AssemblyVersioned(informationalVersion)));
        Assert.Contains("SourceRevisionId", failure.Message, StringComparison.Ordinal);
    }
}
