using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Analysis;

namespace CivicLens.Tests.Core.Analysis;

public sealed class AnalysisRunInputTests
{
    [Theory]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void TheTextHashIsTheSha256OfTheUtf8TextBecauseTheDatabaseCheckRepeatsIt(string text, string expected)
    {
        Assert.Equal(expected, AnalysisRunInput.TextHash(text));
    }

    [Fact]
    public void TheHashCoversTheBytesOfNonAsciiTextBecauseThePostgresCheckHashesUtf8()
    {
        const string text = "Résumé \"é\" <&> 😀";

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), AnalysisRunInput.TextHash(text));
    }

    [Fact]
    public void TheInputHashesItsSystemPromptAndSchemaButNotTheUserMessageBecauseOnlyThoseTwoShareARow()
    {
        var input = new AnalysisRunInput("system text", "user text", "{\"type\":\"object\"}", "medium", 100);

        Assert.Equal(AnalysisRunInput.TextHash("system text"), input.SystemPromptSha256);
        Assert.Equal(AnalysisRunInput.TextHash("{\"type\":\"object\"}"), input.OutputSchemaSha256);
    }
}
