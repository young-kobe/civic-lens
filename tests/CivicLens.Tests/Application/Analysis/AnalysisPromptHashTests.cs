using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Analysis;

namespace CivicLens.Tests.Application.Analysis;

public sealed class AnalysisPromptHashTests
{
    [Fact]
    public void HashCoversTheCompactUnescapedJsonBytesBecauseStoredPromptHashesDependOnThem()
    {
        const string canonical = """{"model":"m","effort":"low","maxTokens":10,"system":"s \"é\" <&>","user":"u","schema":"{}"}""";
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        Assert.Equal(expected, AnalysisPromptHash.Compute("m", "low", 10, "s \"é\" <&>", "u", "{}"));
    }
}
