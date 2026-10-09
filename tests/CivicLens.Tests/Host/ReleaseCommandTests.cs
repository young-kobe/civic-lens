using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Host.Publication;

namespace CivicLens.Tests.Host;

public sealed class ReleaseCommandTests
{
    [Theory]
    [InlineData("releases", "publish", "key")]
    [InlineData("releases", "list", "abc")]
    [InlineData("releases", "list", "0")]
    [InlineData("releases", "list", "5", "6")]
    [InlineData("releases", "activate")]
    [InlineData("releases", "activate", "x")]
    [InlineData("releases", "activate", "0")]
    [InlineData("releases", "activate", "-1")]
    [InlineData("releases", "unknown")]
    public async Task InvalidInputExitsWithTwoBeforeAnyHandlerRuns(params string[] args)
    {
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);

        var exit = await ReleaseCommand.ExecuteAsync(args, null!, null!, null!, owner, CancellationToken.None);

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task OutOfRangeListLimitShowsTheHandlerRuleToTheOwner()
    {
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var list = new ListPublicationReleases(null!, null!);
        var error = new StringWriter();
        var original = Console.Error;
        Console.SetError(error);
        try
        {
            var exit = await ReleaseCommand.ExecuteAsync(["releases", "list", "101"], null!, list, null!, owner, CancellationToken.None);

            Assert.Equal(2, exit);
            Assert.Contains("Page limit must be from 1 to 100.", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void MatchesOnlyReleaseCommands()
    {
        Assert.True(ReleaseCommand.Matches(["releases", "list"]));
        Assert.False(ReleaseCommand.Matches(["documents", "get", "x"]));
    }
}
