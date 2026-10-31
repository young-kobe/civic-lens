using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Core.Review;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

public sealed class ReleaseCommandTests
{
    [Theory]
    [InlineData("releases", "publish", "key", "abc")]
    [InlineData("releases", "publish", "key", "draft:abc")]
    [InlineData("releases", "publish", "key", "statement:")]
    [InlineData("releases", "publish", "key", "Statement:abc")]
    [InlineData("releases", "publish", "key", "--withdraw")]
    [InlineData("releases", "publish", "key", "--withdraw", "statement:abc")]
    [InlineData("releases", "publish", "key", "--withdraw", "draft:abc", "text")]
    [InlineData("releases", "blocked", "1")]
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

        var exit = await ReleaseCommand.ExecuteAsync(args, null!, null!, null!, null!, owner, CancellationToken.None);

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task PublishingRefusesToStartWithoutTheArtifactDirectoryBecauseANewPhotoNeedsItsCapture()
    {
        var folder = Directory.CreateTempSubdirectory("release-command").FullName;
        try
        {
            var collection = Path.Combine(folder, "sources.json");
            var issues = Path.Combine(folder, "issues.json");
            await File.WriteAllTextAsync(collection, """{"version":1,"people":[],"sources":[]}""");
            await File.WriteAllTextAsync(issues, System.Text.Json.JsonSerializer.Serialize(
                CivicLens.Application.Issues.IssueConfiguration.None, CivicLens.Collection.Contracts.CollectionProtocol.JsonOptions));
            var environment = new Dictionary<string, string>
            {
                ["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner",
                ["CIVIC_LENS_RELEASE_DIRECTORY"] = Path.Combine(folder, "releases"),
                ["CIVIC_LENS_COLLECTION_CONFIG"] = collection,
                ["CIVIC_LENS_ISSUES_CONFIG"] = issues
            };
            const string unreachable = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";
            string[] publish = ["releases", "publish", "key", "statement:" + new string('a', 64)];

            var without = await HostProcess.RunWithEnvironmentAsync(unreachable, null, environment, publish);
            environment["CIVIC_LENS_ARTIFACT_DIRECTORY"] = Path.Combine(folder, "captures");
            var with = await HostProcess.RunWithEnvironmentAsync(unreachable, null, environment, publish);

            Assert.Equal(2, without.ExitCode);
            Assert.Equal(1, with.ExitCode);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task PublishingRefusesAnInvalidPublicBaseUrlBeforeAnyWorkSoNoReleaseCarriesAWrongCanonicalAddress()
    {
        var folder = Directory.CreateTempSubdirectory("release-command").FullName;
        try
        {
            var collection = Path.Combine(folder, "sources.json");
            var issues = Path.Combine(folder, "issues.json");
            await File.WriteAllTextAsync(collection, """{"version":1,"people":[],"sources":[]}""");
            await File.WriteAllTextAsync(issues, System.Text.Json.JsonSerializer.Serialize(
                CivicLens.Application.Issues.IssueConfiguration.None, CivicLens.Collection.Contracts.CollectionProtocol.JsonOptions));
            var environment = new Dictionary<string, string>
            {
                ["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner",
                ["CIVIC_LENS_RELEASE_DIRECTORY"] = Path.Combine(folder, "releases"),
                ["CIVIC_LENS_COLLECTION_CONFIG"] = collection,
                ["CIVIC_LENS_ISSUES_CONFIG"] = issues,
                ["CIVIC_LENS_ARTIFACT_DIRECTORY"] = Path.Combine(folder, "captures"),
                ["CIVIC_LENS_PUBLIC_BASE_URL"] = "https://civic.example/site"
            };
            const string unreachable = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";

            string[] publish = ["releases", "publish", "key", "statement:" + new string('a', 64)];

            var invalid = await HostProcess.RunWithEnvironmentAsync(unreachable, null, environment, publish);
            environment["CIVIC_LENS_PUBLIC_BASE_URL"] = "https://civic.example";
            var valid = await HostProcess.RunWithEnvironmentAsync(unreachable, null, environment, publish);

            Assert.Equal(2, invalid.ExitCode);
            Assert.Equal(1, valid.ExitCode);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
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
            var exit = await ReleaseCommand.ExecuteAsync(["releases", "list", "101"], null!, list, null!, null!, owner, CancellationToken.None);

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
                scenario.Publisher(), null!, null!, null!, PublicationScenario.Owner, CancellationToken.None);

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
    public async Task AReplacementPublishesWithTheNoteOfItsApprovalAndWithoutOneExitsWithTwo()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a');
        var reference = $"action:{action}";
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", reference], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        scenario.ReviseAction(action, 2, correctionNote: null);

        var missing = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", reference], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        scenario.ReviseAction(action, 3, correctionNote: "Fixed the issue.");
        var noted = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", reference],
            scenario.Publisher(), null!, null!, null!, PublicationScenario.Owner, CancellationToken.None);

        Assert.Equal((2, 0), (missing, noted));
        var record = JsonSerializer.Deserialize<PublishedAction>(
            scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.RecordDataPath(action)],
            PublicationProtocol.JsonOptions)!;
        Assert.Equal("Fixed the issue.", Assert.Single(record.Corrections).Note);
    }

    [Fact]
    public async Task TheRewriteOfficialsOptionPublishesWithNoRecordAndWritesTheOfficialPageAgain()
    {
        var scenario = new PublicationScenario();
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", $"vote:{scenario.AddVote('a')}"], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        scenario.Releases.WriteOrder.Clear();

        var plain = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2"], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        scenario.Releases.WriteOrder.Clear();
        var rewritten = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", "--rewrite-officials"], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);

        Assert.Equal((2, 0), (plain, rewritten));
        Assert.Contains(PublicationProtocol.OfficialPagePath("mayor"), scenario.Releases.WriteOrder);
    }

    [Fact]
    public async Task PublishWithOnlyAKeyAfterAWithholdFlagMakesAReleaseWithNoPhotoSoATakedownNeedsNoNewRecord()
    {
        var scenario = new PublicationScenario();
        scenario.Captures.Add("attempt-1", [.. MemberPhotoFixtures.Jpeg(), 1]);
        scenario.Photos = [new("mayor", MemberPhotoFixtures.PhotoUrl(), MemberPhoto.MemberGuideCredit, "attempt-1", new string('a', 64))];
        var vote = $"vote:{scenario.AddVote('a')}";
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", vote], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        scenario.WithheldPhotos.Add("mayor");

        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2"], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);

        Assert.Equal(0, exit);
        var files = scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName];
        var officials = JsonSerializer.Deserialize<PublishedOfficials>(files[PublicationProtocol.OfficialsPath], PublicationProtocol.JsonOptions)!;
        Assert.Null(officials.Officials.Single(profile => profile.Id == "mayor").Photo);
    }

    [Fact]
    public async Task WithdrawOptionWithdrawsTheNamedRecordWithoutAnyRecordToPublishAndAnUnpublishedRecordExitsWithTwo()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a');
        var reference = $"action:{action}";
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", reference], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);

        var unknown = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--withdraw", $"action:{new string('b', 64)}", "Wrong."],
            scenario.Publisher(), null!, null!, null!, PublicationScenario.Owner, CancellationToken.None);
        var withdrawn = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", "--withdraw", reference, "Wrong bill."],
            scenario.Publisher(), null!, null!, null!, PublicationScenario.Owner, CancellationToken.None);

        Assert.Equal((2, 0), (unknown, withdrawn));
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.ManifestPath],
            PublicationProtocol.JsonOptions)!;
        Assert.Equal("Wrong bill.", Assert.Single(manifest.Withdrawals).Note);
        Assert.Empty(manifest.Records);
    }

    [Fact]
    public async Task AWithdrawnRecordReturnsWithTheNoteOfItsNewApprovalAndWithoutOneExitsWithTwo()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a');
        var reference = $"action:{action}";
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", reference], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--withdraw", reference, "Wrong bill."], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        scenario.ReviseAction(action, 2, correctionNote: null);

        var missing = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", reference], scenario.Publisher(), null!, null!, null!,
            PublicationScenario.Owner, CancellationToken.None);
        scenario.ReviseAction(action, 3, correctionNote: "Bill fixed.");
        var returned = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k4", reference],
            scenario.Publisher(), null!, null!, null!, PublicationScenario.Owner, CancellationToken.None);

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
                scenario.Publisher(), null!, null!, null!, PublicationScenario.Owner, CancellationToken.None);

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
                null!, null!, null!, null!, new ReviewActor("auth0|owner", ReviewRole.Owner), CancellationToken.None);

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

    [Fact]
    public async Task BlockedListsThePublishedRecordsThatReviewBlockedSoTheOwnerKnowsWhatToWithdraw()
    {
        var scenario = new PublicationScenario();
        var action = new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('a'));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", action));
        scenario.Publications.ReviewStatuses[action] = ReviewStatus.ChangesRequested;

        var json = await RunAsync(["releases", "blocked"], scenario);

        var item = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(("action", action.RecordId, 1, "changesRequested"),
            (item.GetProperty("kind").GetString(), item.GetProperty("recordId").GetString(),
                item.GetProperty("publishedRevisionNumber").GetInt32(), item.GetProperty("reviewStatus").GetString()));
    }

    [Fact]
    public async Task BlockedNamesTheReasonOfEachRowSoTheOwnerSeesAStuckRecordIsNotOneToWithdraw()
    {
        var scenario = new PublicationScenario();
        var blocked = new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('a'));
        var stuck = new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('b'));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", blocked, stuck));
        for (var revision = 2; revision <= PublishedCorrection.MaximumNotedEntries + 1; revision++)
        {
            scenario.ReviseAction(stuck.RecordId, revision, correctionNote: "Fix.");
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request($"k{revision}", stuck));
        }
        scenario.Publications.ReviewStatuses[blocked] = ReviewStatus.ChangesRequested;
        scenario.Publications.ReviewStatuses[stuck] = ReviewStatus.Approved;
        scenario.Publications.UnpublishedApprovalActors[stuck] = "auth0|editor";

        var json = await RunAsync(["releases", "blocked"], scenario);

        var reasons = json.RootElement.EnumerateArray()
            .ToDictionary(item => item.GetProperty("recordId").GetString()!, item => item.GetProperty("reason").GetString());
        Assert.Equal("reviewBlocksIt", reasons[blocked.RecordId]);
        Assert.Equal("correctionLimit", reasons[stuck.RecordId]);
    }

    [Fact]
    public async Task ActivateNestsTheSummaryNextToTheRollbackWarningsSoOneObjectCarriesBoth()
    {
        var scenario = new PublicationScenario();
        var action = new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('a'));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", action));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(action.Kind, action.RecordId, "Wrong.")] });

        var json = await RunAsync(["releases", "activate", "1"], scenario);

        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("summary").GetProperty("releaseNumber").GetInt32());
        Assert.Equal(action.RecordId, Assert.Single(root.GetProperty("restoredWithdrawals").EnumerateArray()).GetProperty("recordId").GetString());
        Assert.Empty(root.GetProperty("withdrawnAgain").EnumerateArray());
        Assert.Empty(root.GetProperty("blocked").EnumerateArray());
    }

    private static async Task<JsonDocument> RunAsync(string[] args, PublicationScenario scenario)
    {
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var exit = await ReleaseCommand.ExecuteAsync(args, null!, null!, scenario.BlockedLister(), scenario.Activator(),
                PublicationScenario.Owner, CancellationToken.None);
            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetOut(original);
        }
        return JsonDocument.Parse(output.ToString());
    }
}
