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
    public void ABuildWithNoUsableShaIsUnversionedAndNeverBreaksTheETagSyntax(string informationalVersion)
    {
        Assert.Equal(BuildId.Unversioned, BuildId.FromAssembly(TestCodeVersion.AssemblyVersioned(informationalVersion)));
    }
}
