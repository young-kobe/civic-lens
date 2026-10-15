using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;

namespace CivicLens.Tests.Host;

public sealed class ReleaseCommandTests
{
    [Theory]
    [InlineData("releases", "publish", "key")]
    [InlineData("releases", "publish", "key", "abc")]
    [InlineData("releases", "publish", "key", "draft:abc")]
    [InlineData("releases", "publish", "key", "statement:")]
    [InlineData("releases", "publish", "key", "Statement:abc")]
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
    public async Task PublishTakesExplicitKindAndIdPairsSoOneReleaseHoldsBothKinds()
    {
        var scenario = new PublicationScenario();
        var change = scenario.AddDraft('a');
        var statement = scenario.AddStatement('b');
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var exit = await ReleaseCommand.ExecuteAsync(
                ["releases", "publish", "key", $"statement:{statement}", $"documentChange:{change}"],
                scenario.Publisher(), null!, null!, PublicationScenario.Owner, CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetOut(original);
        }

        var files = scenario.Releases.Directories.Values.Single();
        Assert.Contains(PublicationProtocol.RecordPagePath(statement), files.Keys);
        Assert.Contains(PublicationProtocol.RecordPagePath(change), files.Keys);
    }

    [Fact]
    public async Task AnUnknownKindIsAUsageErrorThatNamesTheValidKinds()
    {
        var error = new StringWriter();
        var original = Console.Error;
        Console.SetError(error);
        try
        {
            var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "key", "draft:" + new string('a', 32)],
                null!, null!, null!, new ReviewActor("auth0|owner", ReviewRole.Owner), CancellationToken.None);

            Assert.Equal(2, exit);
            Assert.Contains("documentChange, statement, action", error.ToString(), StringComparison.Ordinal);
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
