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
    [InlineData("releases", "entries", "backfill")]
    [InlineData("releases", "entries", "backfill", "0")]
    [InlineData("releases", "entries", "backfill", "x")]
    [InlineData("releases", "entries", "backfill", "5", "6")]
    [InlineData("releases", "corrections", "verify")]
    [InlineData("releases", "corrections", "verify", "0")]
    [InlineData("releases", "corrections", "verify", "5", "bad-cursor")]
    public async Task InvalidInputExitsWithTwoBeforeAnyHandlerRuns(params string[] args)
    {
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);

        var exit = await ReleaseCommand.ExecuteAsync(args, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task PublishingRefusesToStartWithoutTheArtifactDirectoryOrTheIssueIconFileBecauseANewPhotoNeedsItsCaptureAndATileNeedsItsIcon()
    {
        var folder = Directory.CreateTempSubdirectory("release-command").FullName;
        try
        {
            var collection = Path.Combine(folder, "sources.json");
            var issues = Path.Combine(folder, "issues.json");
            await File.WriteAllTextAsync(collection, """{"version":1,"people":[],"sources":[]}""");
            await File.WriteAllTextAsync(issues, System.Text.Json.JsonSerializer.Serialize(
                CivicLens.Application.Issues.IssueConfiguration.None, CivicLens.Collection.Contracts.CollectionProtocol.JsonOptions));
            var icons = Path.Combine(folder, "issue-icons.json");
            await File.WriteAllTextAsync(icons, System.Text.Json.JsonSerializer.Serialize(
                CivicLens.Application.Issues.IssueIconConfiguration.None, CivicLens.Collection.Contracts.CollectionProtocol.JsonOptions));
            var environment = new Dictionary<string, string>
            {
                ["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner",
                ["CIVIC_LENS_RELEASE_DIRECTORY"] = Path.Combine(folder, "releases"),
                ["CIVIC_LENS_COLLECTION_CONFIG"] = collection,
                ["CIVIC_LENS_ISSUES_CONFIG"] = issues,
                ["CIVIC_LENS_ISSUE_ICONS_CONFIG"] = icons
            };
            const string unreachable = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";
            string[] publish = ["releases", "publish", "key", "statement:" + new string('a', 64)];

            var without = await HostProcess.RunWithEnvironmentAsync(unreachable, null, environment, publish);
            environment["CIVIC_LENS_ARTIFACT_DIRECTORY"] = Path.Combine(folder, "captures");
            var with = await HostProcess.RunWithEnvironmentAsync(unreachable, null, environment, publish);

            environment.Remove("CIVIC_LENS_ISSUE_ICONS_CONFIG");
            var withoutIcons = await HostProcess.RunWithEnvironmentAsync(unreachable, null, environment, publish);

            Assert.Equal(2, without.ExitCode);
            Assert.Equal(1, with.ExitCode);
            Assert.Equal(2, withoutIcons.ExitCode);
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
            var icons = Path.Combine(folder, "issue-icons.json");
            await File.WriteAllTextAsync(icons, System.Text.Json.JsonSerializer.Serialize(
                CivicLens.Application.Issues.IssueIconConfiguration.None, CivicLens.Collection.Contracts.CollectionProtocol.JsonOptions));
            var environment = new Dictionary<string, string>
            {
                ["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner",
                ["CIVIC_LENS_RELEASE_DIRECTORY"] = Path.Combine(folder, "releases"),
                ["CIVIC_LENS_COLLECTION_CONFIG"] = collection,
                ["CIVIC_LENS_ISSUES_CONFIG"] = issues,
                ["CIVIC_LENS_ISSUE_ICONS_CONFIG"] = icons,
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
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "list", "101"], null!, list, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, owner, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("Page limit must be from 1 to 100.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheOwnerMayRunTheEntriesBackfillBecauseItWritesPublishedFactsAndTheCommandRefusesAReviewer()
    {
        var reviewer = new ReviewActor("auth0|reviewer", ReviewRole.Reviewer);
        var error = new StringWriter();

        var exit = await ReleaseCommand.ExecuteAsync(["releases", "entries", "backfill", "5"], null!, null!, null!, null!, null!,
            new BackfillPublishedEntries(null!, null!), null!, null!, null!, null!, null!, null!, null!, null!, reviewer, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("not the owner", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectionHistoryBackfillIsOwnerOnly()
    {
        var reviewer = new ReviewActor("auth0|reviewer", ReviewRole.Reviewer);
        var error = new StringWriter();

        var exit = await ReleaseCommand.ExecuteAsync(["releases", "corrections", "backfill", "5"], null!, null!, null!, null!, null!,
            null!, null!, null!, null!,
            new BackfillCorrectionHistories(null!, null!), null!, null!, null!, null!, reviewer, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("not the owner", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectionHistoryVerificationIsOwnerOnly()
    {
        var reviewer = new ReviewActor("auth0|reviewer", ReviewRole.Reviewer);
        var error = new StringWriter();

        var exit = await ReleaseCommand.ExecuteAsync(["releases", "corrections", "verify", "5"], null!, null!, null!, null!, null!,
            null!, null!, null!, new VerifyCorrectionHistories(null!, null!), null!, null!, null!, null!, null!, reviewer, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("not the owner", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectionBackfillReturnsACursorThatCanBePassedToTheNextCommand()
    {
        var scenario = new PublicationScenario();
        scenario.Publications.CorrectionBootstrapCandidates.Add(CorrectionCandidate(1, 'a'));
        scenario.Publications.CorrectionBootstrapCandidates.Add(CorrectionCandidate(2, 'b'));
        var backfill = new BackfillCorrectionHistories(scenario.Publications, scenario.Releases);
        var firstOutput = new StringWriter();
        var firstExit = await ReleaseCommand.ExecuteAsync(["releases", "corrections", "backfill", "1"], null!, null!, null!, null!,
            null!, null!, null!, null!, null!, backfill, null!, null!, null!, null!, PublicationScenario.Owner, firstOutput, TextWriter.Null, CancellationToken.None);
        using var firstJson = JsonDocument.Parse(firstOutput.ToString());
        var cursor = firstJson.RootElement.GetProperty("nextCursor").GetString();
        Assert.NotNull(CorrectionHistoryBootstrapCursor.Parse(cursor));

        var secondOutput = new StringWriter();
        var secondExit = await ReleaseCommand.ExecuteAsync(["releases", "corrections", "backfill", "1", cursor!], null!, null!, null!, null!,
            null!, null!, null!, null!, null!, backfill, null!, null!, null!, null!, PublicationScenario.Owner, secondOutput, TextWriter.Null, CancellationToken.None);
        using var secondJson = JsonDocument.Parse(secondOutput.ToString());

        Assert.Equal(1, firstExit);
        Assert.Equal(2, secondJson.RootElement.GetProperty("failures")[0].GetProperty("releaseNumber").GetInt32());
        Assert.Equal(1, secondExit);
    }

    [Fact]
    public async Task CorrectionVerificationPrintsTheReleaseLedgerFieldsAndAcceptsItsOwnCursor()
    {
        var scenario = new PublicationScenario();
        scenario.Publications.CorrectionBootstrapCandidates.Add(CorrectionCandidate(1, 'a'));
        scenario.Publications.CorrectionBootstrapCandidates.Add(CorrectionCandidate(2, 'b'));
        var verify = new VerifyCorrectionHistories(scenario.Publications, scenario.Releases);
        var firstOutput = new StringWriter();
        var firstExit = await RunVerifyAsync(["releases", "corrections", "verify", "1"], verify, firstOutput);
        using var firstJson = JsonDocument.Parse(firstOutput.ToString());
        var cursor = firstJson.RootElement.GetProperty("nextCursor").GetString();

        var secondOutput = new StringWriter();
        var secondExit = await RunVerifyAsync(["releases", "corrections", "verify", "1", cursor!], verify, secondOutput);
        using var secondJson = JsonDocument.Parse(secondOutput.ToString());

        Assert.Equal((1, 1), (firstExit, secondExit));
        Assert.Equal(0, firstJson.RootElement.GetProperty("verifiedReleases").GetArrayLength());
        Assert.Equal(0, firstJson.RootElement.GetProperty("skippedReleases").GetArrayLength());
        var selection = firstJson.RootElement.GetProperty("selection");
        Assert.Equal([1, 2], selection.EnumerateArray().Select(item => item.GetProperty("releaseNumber").GetInt32()));
        Assert.Equal($"{2:D6}-{new string('a', 32)}", selection[1].GetProperty("directoryName").GetString());
        Assert.Equal(2, CorrectionHistoryVerificationCursor.Parse(cursor)!.ReleaseNumber);
        Assert.Equal(2, CorrectionHistoryVerificationCursor.Parse(selection[1].GetProperty("cursor").GetString())!.ReleaseNumber);
        Assert.Equal(2, secondJson.RootElement.GetProperty("failures")[0].GetProperty("releaseNumber").GetInt32());
        Assert.Equal(JsonValueKind.Null, secondJson.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task CorrectionVerificationReportsASkippedReleaseWithoutFailingTheExitCode()
    {
        var scenario = new PublicationScenario();
        var retired = new CorrectionHistoryVerificationCursor(9, $"{9:D6}-{new string('a', 32)}");
        var output = new StringWriter();

        var exit = await RunVerifyAsync(["releases", "corrections", "verify", "5", retired.Encode()],
            new VerifyCorrectionHistories(scenario.Publications, scenario.Releases), output);
        using var json = JsonDocument.Parse(output.ToString());

        Assert.Equal(0, exit);
        var skipped = json.RootElement.GetProperty("skippedReleases")[0];
        Assert.Equal((9, "notSelected"), (skipped.GetProperty("releaseNumber").GetInt32(), skipped.GetProperty("reason").GetString()));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.Equal(0, json.RootElement.GetProperty("selection").GetArrayLength());
    }

    private static Task<int> RunVerifyAsync(string[] args, VerifyCorrectionHistories verify, TextWriter output) =>
        ReleaseCommand.ExecuteAsync(args, null!, null!, null!, null!, null!, null!, null!, null!, verify, null!, null!, null!, null!, null!,
            PublicationScenario.Owner, output, TextWriter.Null, CancellationToken.None);

    [Theory]
    [InlineData("backfill")]
    [InlineData("verify")]
    public async Task BillCountCommandsAreOwnerOnly(string command)
    {
        var reviewer = new ReviewActor("auth0|reviewer", ReviewRole.Reviewer);
        var error = new StringWriter();

        var exit = await RunCountsAsync(["releases", "counts", command, "5"], new(null!, null!), new(null!, null!), reviewer, TextWriter.Null, error);

        Assert.Equal(2, exit);
        Assert.Contains("not the owner", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BillCountBackfillReturnsAReleaseNumberCursorThatCanBePassedToTheNextCommand()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        var backfill = new BackfillReleaseBillCounts(scenario.Publications, scenario.Releases);
        var firstOutput = new StringWriter();
        var firstExit = await RunCountsAsync(["releases", "counts", "backfill", "1"], backfill, null!, PublicationScenario.Owner, firstOutput, TextWriter.Null);
        using var firstJson = JsonDocument.Parse(firstOutput.ToString());
        var cursor = firstJson.RootElement.GetProperty("nextCursor").GetInt32();

        var secondOutput = new StringWriter();
        var secondExit = await RunCountsAsync(["releases", "counts", "backfill", "1", cursor.ToString()], backfill, null!, PublicationScenario.Owner,
            secondOutput, TextWriter.Null);
        using var secondJson = JsonDocument.Parse(secondOutput.ToString());

        Assert.Equal((0, 0, 1), (firstExit, secondExit, cursor));
        Assert.Equal(JsonValueKind.Null, secondJson.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.Equal(new[] { 1, 2 }, scenario.Publications.CommittedBillCounts.Select(item => item.ReleaseNumber));
    }

    [Fact]
    public async Task BillCountBackfillExitsWithOneWhenAReleaseFails()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.BillCountCommitFailures[1] = new BillCountRefusedException("Partial snapshot.");
        var backfill = new BackfillReleaseBillCounts(scenario.Publications, scenario.Releases);

        var exit = await RunCountsAsync(["releases", "counts", "backfill", "1"], backfill, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task BillCountVerifyExitsWithOneOnAMissingRowAndWithZeroWhenEveryReleaseMatches()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        var verify = new VerifyReleaseBillCounts(scenario.Publications, scenario.Releases);

        var matching = await RunCountsAsync(["releases", "counts", "verify", "5"], null!, verify, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null);
        scenario.Publications.BillCountStatuses[1] = BillCountStatus.MissingRows;
        var missing = await RunCountsAsync(["releases", "counts", "verify", "5"], null!, verify, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null);

        Assert.Equal((0, 1), (matching, missing));
    }

    [Fact]
    public async Task BillCountVerifyCursorFromTheOutputContinuesTheScanAndABadCursorExitsWithTwo()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        var verify = new VerifyReleaseBillCounts(scenario.Publications, scenario.Releases);
        var firstOutput = new StringWriter();
        await RunCountsAsync(["releases", "counts", "verify", "1"], null!, verify, PublicationScenario.Owner, firstOutput, TextWriter.Null);
        using var firstJson = JsonDocument.Parse(firstOutput.ToString());
        var cursor = firstJson.RootElement.GetProperty("nextCursor").GetString()!;

        var continued = await RunCountsAsync(["releases", "counts", "verify", "1", cursor], null!, verify, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null);
        var rejected = await RunCountsAsync(["releases", "counts", "verify", "1", "nope"], null!, verify, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null);

        Assert.Equal((0, 2), (continued, rejected));
        Assert.Equal(new[] { 1, 2 }, scenario.Publications.VerifiedBillCounts);
    }

    [Theory]
    [InlineData("backfill 0")]
    [InlineData("backfill 5 0")]
    [InlineData("backfill 5 1 2")]
    [InlineData("verify 0")]
    [InlineData("verify 5 a b")]
    [InlineData("check 5")]
    public async Task BillCountCommandWithBadArgumentsPrintsTheUsageLine(string arguments)
    {
        var error = new StringWriter();

        var exit = await RunCountsAsync(["releases", "counts", .. arguments.Split(' ')], new(null!, null!), new(null!, null!), PublicationScenario.Owner, TextWriter.Null, error);

        Assert.Equal(2, exit);
        Assert.Contains("releases counts backfill <limit> [release-number], releases counts verify <limit> [cursor]", error.ToString(), StringComparison.Ordinal);
    }

    private static Task<int> RunCountsAsync(string[] args, BackfillReleaseBillCounts backfill, VerifyReleaseBillCounts verify, ReviewActor actor,
        TextWriter output, TextWriter error) => ReleaseCommand.ExecuteAsync(args, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        backfill, verify, new(null!, null!), new(null!, null!), actor, output, error, CancellationToken.None);

    [Theory]
    [InlineData("backfill")]
    [InlineData("verify")]
    public async Task PartyLineUpCommandsAreOwnerOnly(string command)
    {
        var reviewer = new ReviewActor("auth0|reviewer", ReviewRole.Reviewer);
        var error = new StringWriter();

        var exit = await RunLineUpsAsync(["releases", "lineups", command, "5"], new(null!, null!), new(null!, null!), reviewer, TextWriter.Null, error);

        Assert.Equal(2, exit);
        Assert.Contains("not the owner", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartyLineUpBackfillReturnsAReleaseNumberCursorAndExitsWithOneWhenAReleaseFails()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        var backfill = new BackfillReleasePartyLineUps(scenario.Publications, scenario.Releases);
        var firstOutput = new StringWriter();
        var firstExit = await RunLineUpsAsync(["releases", "lineups", "backfill", "1"], backfill, null!, PublicationScenario.Owner, firstOutput, TextWriter.Null);
        using var firstJson = JsonDocument.Parse(firstOutput.ToString());
        var cursor = firstJson.RootElement.GetProperty("nextCursor").GetInt32();
        scenario.Publications.PartyLineUpCommitFailures[2] = new BillCountRefusedException("Partial snapshot.");

        var secondExit = await RunLineUpsAsync(["releases", "lineups", "backfill", "1", cursor.ToString()], backfill, null!, PublicationScenario.Owner,
            TextWriter.Null, TextWriter.Null);

        Assert.Equal((0, 1, 1), (firstExit, secondExit, cursor));
        Assert.Empty(scenario.Publications.CommittedBillCounts);
    }

    [Fact]
    public async Task PartyLineUpBackfillAndVerifyReportSkippedReleasesApartFromConvertedOnes()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.NoPartyLineUpRows.Add(1);
        scenario.Publications.PartyLineUpStatuses[1] = BillCountStatus.NothingExpected;
        var backfillOutput = new StringWriter();
        var verifyOutput = new StringWriter();

        var backfillExit = await RunLineUpsAsync(["releases", "lineups", "backfill", "5"], new BackfillReleasePartyLineUps(scenario.Publications, scenario.Releases),
            null!, PublicationScenario.Owner, backfillOutput, TextWriter.Null);
        var verifyExit = await RunLineUpsAsync(["releases", "lineups", "verify", "5"], null!, new VerifyReleasePartyLineUps(scenario.Publications, scenario.Releases),
            PublicationScenario.Owner, verifyOutput, TextWriter.Null);

        using var backfillJson = JsonDocument.Parse(backfillOutput.ToString());
        using var verifyJson = JsonDocument.Parse(verifyOutput.ToString());
        Assert.Equal((0, 0), (backfillExit, verifyExit));
        Assert.Equal((1, 0, 0), (backfillJson.RootElement.GetProperty("skipped").GetInt32(), backfillJson.RootElement.GetProperty("added").GetInt32(),
            backfillJson.RootElement.GetProperty("alreadyPresent").GetInt32()));
        Assert.Equal((1, 0), (verifyJson.RootElement.GetProperty("skipped").GetInt32(), verifyJson.RootElement.GetProperty("verified").GetInt32()));
    }

    [Fact]
    public async Task PartyLineUpVerifyExitsWithOneOnAMissingRowAndTwoOnABadCursorAndItsCursorContinuesTheScan()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        var verify = new VerifyReleasePartyLineUps(scenario.Publications, scenario.Releases);
        var firstOutput = new StringWriter();
        var matching = await RunLineUpsAsync(["releases", "lineups", "verify", "1"], null!, verify, PublicationScenario.Owner, firstOutput, TextWriter.Null);
        using var firstJson = JsonDocument.Parse(firstOutput.ToString());
        var cursor = firstJson.RootElement.GetProperty("nextCursor").GetString()!;
        scenario.Publications.PartyLineUpStatuses[2] = BillCountStatus.MissingRows;

        var missing = await RunLineUpsAsync(["releases", "lineups", "verify", "1", cursor], null!, verify, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null);
        var rejected = await RunLineUpsAsync(["releases", "lineups", "verify", "1", "nope"], null!, verify, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null);

        Assert.Equal((0, 1, 2), (matching, missing, rejected));
        Assert.Empty(scenario.Publications.VerifiedBillCounts);
    }

    [Theory]
    [InlineData("backfill 0")]
    [InlineData("backfill 5 0")]
    [InlineData("backfill 5 1 2")]
    [InlineData("verify 0")]
    [InlineData("verify 5 a b")]
    [InlineData("check 5")]
    public async Task PartyLineUpCommandWithBadArgumentsPrintsTheUsageLine(string arguments)
    {
        var error = new StringWriter();

        var exit = await RunLineUpsAsync(["releases", "lineups", .. arguments.Split(' ')], new(null!, null!), new(null!, null!), PublicationScenario.Owner, TextWriter.Null, error);

        Assert.Equal(2, exit);
        Assert.Contains("releases lineups backfill <limit> [release-number], releases lineups verify <limit> [cursor]", error.ToString(), StringComparison.Ordinal);
    }

    private static Task<int> RunLineUpsAsync(string[] args, BackfillReleasePartyLineUps backfill, VerifyReleasePartyLineUps verify, ReviewActor actor,
        TextWriter output, TextWriter error) => ReleaseCommand.ExecuteAsync(args, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        new(null!, null!), new(null!, null!), backfill, verify, actor, output, error, CancellationToken.None);

    private static CorrectionHistoryBootstrapCandidate CorrectionCandidate(int releaseNumber, char id)
    {
        var record = new StoredReleaseRecord(PublishedRecordKind.Action, new string(id, 64), 1, DateTimeOffset.UnixEpoch,
            new PublishedEntryFacts("Headline", null, null, null, null, null, null, null, null, [], null, [], [], null));
        return new(new PublicationReleaseSummary(releaseNumber, $"{releaseNumber:D6}-{new string('a', 32)}",
            DateTimeOffset.UnixEpoch.AddDays(releaseNumber), 1), record);
    }

    [Fact]
    public async Task TheFirstPublishedBackfillPrintsItsCountsAsJsonAndAnExtraArgumentExitsWithTwo()
    {
        var scenario = new PublicationScenario();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", scenario.AddDraft('a')));
        scenario.Publications.FirstPublished.Clear();
        var backfill = new BackfillFirstPublished(scenario.Publications, scenario.Releases);
        var output = new StringWriter();

        var exit = await ReleaseCommand.ExecuteAsync(["releases", "first-published", "backfill"], null!, null!, null!, null!, backfill,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, output, TextWriter.Null, CancellationToken.None);
        var extra = await ReleaseCommand.ExecuteAsync(["releases", "first-published", "backfill", "5"], null!, null!, null!, null!, backfill,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        Assert.Equal((0, 2), (exit, extra));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal((1, 1, 0), (json.RootElement.GetProperty("examined").GetInt32(), json.RootElement.GetProperty("added").GetInt32(),
            json.RootElement.GetProperty("alreadyPresent").GetInt32()));
    }

    [Fact]
    public async Task APublishThatNeedsTheBackfillExitsWithOneAndNamesTheCommand()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a');
        var reference = $"action:{action}";
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", reference], scenario.Publisher(), null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.Publications.FirstPublished.Clear();
        scenario.ReviseAction(action, 2);
        var error = new StringWriter();

        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", reference], scenario.Publisher(), null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("releases first-published backfill", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishTakesExplicitKindAndIdPairsSoOneReleaseHoldsBothKinds()
    {
        var scenario = new PublicationScenario();
        var change = scenario.AddDraft('a');
        var statement = scenario.AddStatement('b');
        var exit = await ReleaseCommand.ExecuteAsync(
            ["releases", "publish", "key", $"statement:{statement}", $"documentChange:{change}"],
            scenario.Publisher(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        Assert.Equal(0, exit);

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
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.ReviseAction(action, 2, correctionNote: null);

        var missing = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", reference], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.ReviseAction(action, 3, correctionNote: "Fixed the issue.");
        var noted = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", reference],
            scenario.Publisher(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

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
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.Releases.WriteOrder.Clear();

        var plain = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.Releases.WriteOrder.Clear();
        var rewritten = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", "--rewrite-officials"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        Assert.Equal((2, 0), (plain, rewritten));
        Assert.Contains(PublicationProtocol.OfficialPagePath("mayor"), scenario.Releases.WriteOrder);
    }

    [Fact]
    public async Task TheRewriteEntriesOptionPublishesWithNoRecordAndFillsTheVoteFactsOfACarriedVote()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('a');
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", $"vote:{vote}"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        var first = scenario.Publications.Committed[^1].DirectoryName;
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(scenario.Releases.Directories[first][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
        scenario.Releases.Directories[first][PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(
            manifest with { Records = [.. manifest.Records.Select(entry => entry with { YeaCount = null, NayCount = null, Alignment = null })] },
            PublicationProtocol.JsonOptions);

        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--rewrite-entries"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        Assert.Equal(0, exit);
        var rewritten = JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
        Assert.NotNull(Assert.Single(rewritten.Records).YeaCount);
    }

    [Fact]
    public async Task TheRewritePagesOptionPublishesWithNoRecordAndRendersTheCarriedPageAgain()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('a');
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", $"statement:{statement}"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.PageLayout = " layout-2";

        var plain = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        var rewritten = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", "--rewrite-pages"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        Assert.Equal((2, 0), (plain, rewritten));
        var page = scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.RecordPagePath(statement)];
        Assert.EndsWith(" layout-2</html>", System.Text.Encoding.UTF8.GetString(page));
    }

    [Fact]
    public async Task ARewriteOfPagesThatFindsAnInvalidRecordFilePrintsTheKindAndIdAndExitsWithOneSoTheOperatorKnowsWhichFileToCheck()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a');
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", $"action:{action}"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.RecordDataPath(action)] = "not json"u8.ToArray();
        var error = new StringWriter();
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--rewrite-pages"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains($"Action {action}", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARewriteOfPagesThatFindsAnInvalidRelationshipFilePrintsTheRelationshipIdAndExitsWithOne()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('a');
        var action = scenario.AddAction('b');
        var relationship = scenario.AddRelationship('e', statement, action);
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", $"statement:{statement}", $"action:{action}", $"relationship:{relationship}"],
            scenario.Publisher(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.RecordDataPath(relationship)] = "not json"u8.ToArray();
        var error = new StringWriter();
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--rewrite-pages"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains($"Relationship {relationship}", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRewriteEntriesAndRewriteOfficialsOptionsRunTogetherAsInTheRunbookAndFillEntriesAndSummaries()
    {
        var scenario = new PublicationScenario();
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", $"vote:{scenario.AddVote('a')}", $"action:{scenario.AddAction('b', cosponsorWithdrawn: false)}",
            $"statement:{scenario.AddStatement('c', speakerIds: ["chair"])}"],
            scenario.Publisher(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        var first = scenario.Publications.Committed[^1].DirectoryName;
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(scenario.Releases.Directories[first][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
        scenario.Releases.Directories[first][PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(
            manifest with { Records = [.. manifest.Records.Select(entry => entry with { YeaCount = null, NayCount = null, Alignment = null, CosponsorIds = null })] },
            PublicationProtocol.JsonOptions);
        scenario.Releases.WriteOrder.Clear();

        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--rewrite-entries", "--rewrite-officials"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        Assert.Equal(0, exit);
        var directory = scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName];
        var rewritten = JsonSerializer.Deserialize<PublicationRelease>(directory[PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
        Assert.NotNull(rewritten.Records.Single(entry => entry.Kind == PublishedRecordKind.Vote).YeaCount);
        Assert.Equal(["chair"], rewritten.Records.Single(entry => entry.Kind == PublishedRecordKind.Action).CosponsorIds!);
        Assert.Contains(PublicationProtocol.OfficialPagePath("mayor"), scenario.Releases.WriteOrder);
        var summaries = JsonSerializer.Deserialize<PublishedOfficialSummaries>(directory[PublicationProtocol.OfficialSummariesPath], PublicationProtocol.JsonOptions)!;
        Assert.Equal(1, summaries.Officials.Single(row => row.Id == "chair").CosponsoredBills);
    }

    [Fact]
    public async Task ARewriteThatFindsARecordFileOutOfStepWithItsEntryPrintsTheKindAndIdAndExitsWithOneSoTheOperatorKnowsWhichFileToCheck()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('b', cosponsorWithdrawn: false);
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", $"action:{action}"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        var first = scenario.Publications.Committed[^1].DirectoryName;
        scenario.Releases.Directories[first][PublicationProtocol.RecordDataPath(action)] = "not json"u8.ToArray();
        var error = new StringWriter();
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--rewrite-entries"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains($"Action {action}", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishWithOnlyAKeyAfterAWithholdFlagMakesAReleaseWithNoPhotoSoATakedownNeedsNoNewRecord()
    {
        var scenario = new PublicationScenario();
        scenario.Captures.Add("attempt-1", [.. MemberPhotoFixtures.Jpeg(), 1]);
        scenario.Photos = [new("mayor", MemberPhotoFixtures.PhotoUrl(), MemberPhoto.MemberGuideCredit, "attempt-1", new string('a', 64))];
        var vote = $"vote:{scenario.AddVote('a')}";
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k1", vote], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.WithheldPhotos.Add("mayor");

        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2"], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

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
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        var unknown = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--withdraw", $"action:{new string('b', 64)}", "Wrong."],
            scenario.Publisher(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        var withdrawn = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", "--withdraw", reference, "Wrong bill."],
            scenario.Publisher(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

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
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        await ReleaseCommand.ExecuteAsync(["releases", "publish", "k2", "--withdraw", reference, "Wrong bill."], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.ReviseAction(action, 2, correctionNote: null);

        var missing = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k3", reference], scenario.Publisher(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        scenario.ReviseAction(action, 3, correctionNote: "Bill fixed.");
        var returned = await ReleaseCommand.ExecuteAsync(["releases", "publish", "k4", reference],
            scenario.Publisher(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        Assert.Equal((2, 0), (missing, returned));
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName][PublicationProtocol.ManifestPath],
            PublicationProtocol.JsonOptions)!;
        Assert.Equal(action, Assert.Single(manifest.Records).RecordId);
        Assert.Empty(manifest.Withdrawals);
    }

    [Fact]
    public async Task AStateMismatchExitsWithOneAndPrintsItsCountsInsteadOfTheGenericFailure()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        scenario.Publications.FailOnCommit = new PublicationStateMismatchException("Release 2 lists 1 records, but the state table holds 2 rows for it.");
        var error = new StringWriter();
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "key", $"statement:{statement}"],
            scenario.Publisher(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("lists 1 records, but the state table holds 2 rows", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APublishConflictExitsWithOneSoTheOwnerRebuildsInsteadOfFixingInput()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        scenario.Publications.ConflictOnCommit = true;
        var error = new StringWriter();
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "key", $"statement:{statement}"],
            scenario.Publisher(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Review state changed.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownKindIsAUsageErrorThatNamesTheValidKinds()
    {
        var error = new StringWriter();
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "publish", "key", "draft:" + new string('a', 32)],
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, new ReviewActor("auth0|owner", ReviewRole.Owner), TextWriter.Null, error, CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("documentChange, statement, action", error.ToString(), StringComparison.Ordinal);
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
        var exit = await ReleaseCommand.ExecuteAsync(args, null!, null!, scenario.BlockedLister(), scenario.Activator(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, output, TextWriter.Null, CancellationToken.None);
        Assert.Equal(0, exit);
        return JsonDocument.Parse(output.ToString());
    }
}
