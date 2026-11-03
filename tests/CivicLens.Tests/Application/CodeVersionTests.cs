using System.Reflection;
using System.Reflection.Emit;
using CivicLens.Application;

namespace CivicLens.Tests.Application;

public sealed class CodeVersionTests
{
    private static readonly string Sha = new('a', 40);

    [Fact]
    public void AnAssemblyThatCarriesTheShaAfterThePlusGivesItsCodeVersion()
    {
        Assert.Equal(Sha, CodeVersion.FromAssembly(AssemblyVersioned($"1.0.0+{Sha}")).Sha);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+abc123")]
    [InlineData("1.0.0+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void AnAssemblyWithoutAValidShaGivesNoCodeVersionAndReadingItFailsWithAClearError(string version)
    {
        var codeVersion = CodeVersion.FromAssembly(AssemblyVersioned(version));

        var failure = Assert.Throws<InvalidOperationException>(() => codeVersion.Sha);
        Assert.Contains("SourceRevisionId", failure.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    public void ASetCodeVersionIsExactlyFortyLowercaseHexDigits(string value)
    {
        Assert.Throws<ArgumentException>(() => CodeVersion.Of(value));
    }

    [Fact]
    public void TheApplicationAssemblyOfABuildFromACheckoutCarriesItsCommitSha()
    {
        Assert.Matches("^[0-9a-f]{40}$", CodeVersion.FromAssembly(typeof(CodeVersion).Assembly).Sha);
    }

    private static AssemblyBuilder AssemblyVersioned(string informationalVersion) =>
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("versioned"), AssemblyBuilderAccess.Run,
        [
            new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informationalVersion])
        ]);
}
