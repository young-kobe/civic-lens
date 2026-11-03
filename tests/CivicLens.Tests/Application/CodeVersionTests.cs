using CivicLens.Application;
using CivicLens.Host;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application;

public sealed class CodeVersionTests
{
    private static readonly string Sha = new('a', 40);

    [Fact]
    public void AnAssemblyThatCarriesTheShaAfterThePlusGivesItsCodeVersion()
    {
        Assert.Equal(Sha, CodeVersion.FromAssembly(TestCodeVersion.AssemblyVersioned($"1.0.0+{Sha}")).Sha);
    }

    [Fact]
    public void AnAssemblyWithoutAShaGivesNoCodeVersionAndReadingItFailsWithAClearError()
    {
        var codeVersion = CodeVersion.FromAssembly(TestCodeVersion.AssemblyVersioned("1.0.0"));

        var failure = Assert.Throws<InvalidOperationException>(() => codeVersion.Sha);
        Assert.Contains("no git commit SHA", failure.Message);
        Assert.Contains("SourceRevisionId", failure.Message);
    }

    [Theory]
    [InlineData("1.0.0+", "")]
    [InlineData("1.0.0+abc123", "abc123")]
    [InlineData("1.0.0+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1.0.0+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1.0.0+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("1.0.0+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    public void AnAssemblyWithAShaThatIsNotFortyLowercaseHexDigitsFailsWithAnErrorThatNamesTheValue(string version, string value)
    {
        var codeVersion = CodeVersion.FromAssembly(TestCodeVersion.AssemblyVersioned(version));

        var failure = Assert.Throws<InvalidOperationException>(() => codeVersion.Sha);
        Assert.Contains($"'{value}'", failure.Message);
        Assert.DoesNotContain("no git commit SHA", failure.Message);
    }

    [Fact]
    public void TheHostAssemblyOfABuildFromACheckoutCarriesItsCommitSha()
    {
        Assert.Matches("^[0-9a-f]{40}$", CodeVersion.FromAssembly(typeof(BuildCodeVersion).Assembly).Sha);
    }

    [Theory]
    [InlineData("worker", "collector.dll", "artifacts")]
    [InlineData("worker", "collector.dll", "artifacts", "--once")]
    [InlineData("releases", "publish", "key")]
    public void AWorkerAndAReleasePublishRefuseToStartWithoutAValidShaSoNoCodeWriteFailsLaterAfterAModelCall(params string[] args)
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            BuildCodeVersion.ReadFor(args, TestCodeVersion.AssemblyVersioned("1.0.0+abc123")));

        Assert.Contains("'abc123'", failure.Message);
        Assert.Throws<InvalidOperationException>(() => BuildCodeVersion.ReadFor(args, TestCodeVersion.AssemblyVersioned("1.0.0")));
        Assert.Equal(Sha, BuildCodeVersion.ReadFor(args, TestCodeVersion.AssemblyVersioned($"1.0.0+{Sha}")).Sha);
    }

    [Theory]
    [InlineData("worker", "health")]
    [InlineData("releases", "list")]
    [InlineData("releases", "activate", "1")]
    [InlineData("review")]
    public void ACommandThatWritesNoCodeRevisionOrReleaseStartsWithoutAValidSha(params string[] args)
    {
        var codeVersion = BuildCodeVersion.ReadFor(args, TestCodeVersion.AssemblyVersioned("1.0.0"));

        Assert.Throws<InvalidOperationException>(() => codeVersion.Sha);
    }
}
