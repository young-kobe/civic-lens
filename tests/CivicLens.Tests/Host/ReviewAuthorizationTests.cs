using System.Security.Claims;
using CivicLens.Core.Review;
using CivicLens.Host.Review;

namespace CivicLens.Tests.Host;

public sealed class ReviewAuthorizationTests
{
    private readonly ReviewActorAccessor accessor = new(new ReviewWorkspaceSettings(
        new Uri("https://review.example.test"), new Uri("https://tenant.example.test"), "client", "secret",
        "auth0|owner", new HashSet<string>(StringComparer.Ordinal) { "auth0|friend" }, "/tmp/review-test-keys"));

    [Fact]
    public void OnlyExplicitSubjectsReceiveRolesRegardlessOfSuppliedRoleClaims()
    {
        Assert.Equal(ReviewRole.Owner, accessor.GetActor(Principal("auth0|owner")).Role);
        Assert.Equal(ReviewRole.Reviewer, accessor.GetActor(Principal("auth0|friend", new Claim("role", "Owner"))).Role);
        Assert.False(accessor.IsAuthorized(Principal("auth0|stranger", new Claim("role", "Owner"))));
        Assert.False(accessor.IsAuthorized(Principal("AUTH0|OWNER")));
    }

    [Fact]
    public void AnonymousMissingAndAmbiguousSubjectsAreRejected()
    {
        Assert.False(accessor.IsAuthorized(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "auth0|owner")]))));
        Assert.False(accessor.IsAuthorized(new ClaimsPrincipal(new ClaimsIdentity([], "cookie"))));
        Assert.False(accessor.IsAuthorized(Principal("auth0|friend", new Claim("sub", "auth0|owner"))));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task ReviewCommandRejectsInvalidPortsAsync(string port)
    {
        var result = await HostProcess.RunAsync(null, "review", port);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use review", result.Error);
    }

    private static ClaimsPrincipal Principal(string subject, params Claim[] extra) =>
        new(new ClaimsIdentity(new[] { new Claim("sub", subject) }.Concat(extra), "cookie"));
}
