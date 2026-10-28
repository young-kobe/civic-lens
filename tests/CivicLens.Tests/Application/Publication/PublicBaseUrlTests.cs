using CivicLens.Application.Publication;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublicBaseUrlTests
{
    [Theory]
    [InlineData("https://civic.example", "https://civic.example")]
    [InlineData("https://civic.example/", "https://civic.example")]
    [InlineData(" https://civic.example/ ", "https://civic.example")]
    [InlineData("https://civic.example:8443", "https://civic.example:8443")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("http://127.0.0.1", "http://127.0.0.1")]
    [InlineData("https://civic.example/.", "https://civic.example")]
    [InlineData("HTTPS://Civic.Example", "https://civic.example")]
    [InlineData("https://civic.example:443", "https://civic.example")]
    [InlineData("http://localhost:80", "http://localhost")]
    [InlineData("https://m\u00fcnchen.example", "https://xn--mnchen-3ya.example")]
    public void ABareOriginIsAcceptedAndStoredAsTheNormalOriginWithoutATrailingSlashSoEveryJoinHasOneSlash(string value, string expectedOrigin)
    {
        var url = PublicBaseUrl.Parse(value, "VARIABLE");

        Assert.Equal(expectedOrigin + "/", url!.Join(string.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankValueMeansTheSiteHasNoPublicAddressYet(string? value) =>
        Assert.Null(PublicBaseUrl.Parse(value, "VARIABLE"));

    [Theory]
    [InlineData("civic.example")]
    [InlineData("/records")]
    [InlineData("http://civic.example")]
    [InlineData("http://localhost.example")]
    [InlineData("ftp://civic.example")]
    [InlineData("https://user@civic.example")]
    [InlineData("https://civic.example/site")]
    [InlineData("https://civic.example/site/")]
    [InlineData("https://civic.example//")]
    [InlineData("https://civic.example?a=1")]
    [InlineData("https://civic.example/?a=1")]
    [InlineData("https://civic.example?")]
    [InlineData("https://civic.example#top")]
    [InlineData("https://civic.example#")]
    [InlineData("https://civic.example\\")]
    [InlineData("http://[::1]")]
    [InlineData("https://[::1]:8443")]
    public void AnythingBeyondAnHttpsOriginIsRefusedAndTheMessageNamesTheVariableSoTheOwnerCanFixIt(string value)
    {
        var failure = Assert.Throws<ArgumentException>(() => PublicBaseUrl.Parse(value, "CIVIC_LENS_PUBLIC_BASE_URL"));

        Assert.StartsWith("CIVIC_LENS_PUBLIC_BASE_URL must be a bare origin", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "https://civic.example/")]
    [InlineData("search.html", "https://civic.example/search.html")]
    [InlineData("records/abc.html", "https://civic.example/records/abc.html")]
    public void JoinMakesTheHomeAddressAndEveryOtherAddressFromAReleasePath(string path, string expected) =>
        Assert.Equal(expected, PublicBaseUrl.Parse("https://civic.example/", "VARIABLE")!.Join(path));
}
