using System.Text.Json;
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
    [InlineData("releases", "publish", "key", "--note")]
    [InlineData("releases", "publish", "key", "--note", "statement:abc")]
    [InlineData("releases", "publish", "key", "--note", "statement:abc", "text")]
    [InlineData("releases", "publish", "key", "statement:abc", "--note", "draft:abc", "text")]
    [InlineData("releases", "publish", "key", "statement:abc", "--note", "statement:abc")]
    [InlineData("releases", "publish", "key", "--withdraw")]
    [InlineData("releases", "publish", "key", "--withdraw", "statement:abc")]
    [InlineData("releases", "publish", "key", "--withdraw", "draft:abc", "text")]
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
    public async Task NoteOptionsAttachTheirTextToTheNamedRecordSoTheReplacementEntryHoldsIt()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a');
        var reference = $"action:{action}";
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", reference], scenario.Publisher(), null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        scenario.ReviseAction(action, 2);

        var missing = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", reference], scenario.Publisher(), null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        var noted = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", reference, "--note", reference, "Fixed the issue."],
            scenario.Publisher(), null!, null!, PublicationScenario.Owner, CancellationToken.None);

        Assert.Equal((2, 0), (missing, noted));
        var record = JsonSerializer.Deserialize<PublishedAction>(
            scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.RecordDataPath(action)],
            PublicationProtocol.JsonOptions)!;
        Assert.Equal("Fixed the issue.", Assert.Single(record.Corrections).Note);
    }

    [Fact]
    public async Task WithdrawOptionWithdrawsTheNamedRecordWithoutAnyRecordToPublishAndAnUnpublishedRecordExitsWithTwo()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a');
        var reference = $"action:{action}";
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", reference], scenario.Publisher(), null!, null!,
            PublicationScenario.Owner, CancellationToken.None);

        var unknown = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--withdraw", $"action:{new string('b', 64)}", "Wrong."],
            scenario.Publisher(), null!, null!, PublicationScenario.Owner, CancellationToken.None);
        var withdrawn = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", "--withdraw", reference, "Wrong bill."],
            scenario.Publisher(), null!, null!, PublicationScenario.Owner, CancellationToken.None);

        Assert.Equal((2, 0), (unknown, withdrawn));
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.ManifestPath],
            PublicationProtocol.JsonOptions)!;
        Assert.Equal("Wrong bill.", Assert.Single(manifest.Withdrawals).Note);
        Assert.Empty(manifest.Records);
    }

    [Fact]
    public async Task NoteOptionOnAWithdrawnRecordBringsItBackAndNoNoteExitsWithTwo()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a');
        var reference = $"action:{action}";
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", reference], scenario.Publisher(), null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--withdraw", reference, "Wrong bill."], scenario.Publisher(), null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        scenario.ReviseAction(action, 2);

        var missing = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", reference], scenario.Publisher(), null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        var returned = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", reference, "--note", reference, "Bill fixed."],
            scenario.Publisher(), null!, null!, PublicationScenario.Owner, CancellationToken.None);

        Assert.Equal((2, 0), (missing, returned));
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.ManifestPath],
            PublicationProtocol.JsonOptions)!;
        Assert.Equal(action, Assert.Single(manifest.Records).RecordId);
        Assert.Empty(manifest.Withdrawals);
    }

    [Fact]
    public async Task APublishConflictExitsWithOneSoTheOwnerRebuildsInsteadOfFixingInput()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        scenario.Publications.ConflictOnCommit = true;
        var error = new StringWriter();
        var original = Console.Error;
        Console.SetError(error);
        try
        {
            var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "key", $"statement:{statement}"],
                scenario.Publisher(), null!, null!, PublicationScenario.Owner, CancellationToken.None);

            Assert.Equal(1, exit);
            Assert.Contains("Review state changed.", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(original);
        }
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
