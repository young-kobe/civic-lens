using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishEntryRewriteTests
{
    [Fact]
    public async Task AReleaseFromBeforeTheNewFieldsStillReadsAndAPlainPublishKeepsEveryCarriedEntryAsItWas()
    {
        var scenario = new PublicationScenario();
        var first = await PublishFirstAsync(scenario);
        AsReleaseFromBeforeTheNewFields(scenario, first);
        var before = Manifest(scenario, first).Records;

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('d'))));

        var carried = Manifest(scenario, second).Records.Where(entry => before.Any(old => old.RecordId == entry.RecordId)).ToArray();
        Assert.Equal(before.Length, carried.Length);
        foreach (var old in before)
            Assert.Equal(Json(old), Json(carried.Single(entry => entry.RecordId == old.RecordId)));
        Assert.All(carried, entry => Assert.Equal((null, null, null, null), (entry.YeaCount, entry.NayCount, entry.Alignment, entry.CosponsorOfficials)));
    }

    [Fact]
    public async Task TheRewriteOptionFillsTheNewFieldsFromTheRecordFilesAndLeavesEveryOtherFieldAndEveryRecordFileAsItWas()
    {
        var scenario = new PublicationScenario();
        var first = await PublishFirstAsync(scenario);
        AsReleaseFromBeforeTheNewFields(scenario, first);
        var before = Manifest(scenario, first).Records;
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteEntries = true });

        var after = Manifest(scenario, second).Records;
        Assert.Equal(before.Select(entry => entry.RecordId).Order(), after.Select(entry => entry.RecordId).Order());
        foreach (var old in before)
            Assert.Equal(Json(old), Json(WithoutNewFields(after.Single(entry => entry.RecordId == old.RecordId))));
        var vote = after.Single(entry => entry.Kind == PublishedRecordKind.Vote);
        var voteRecord = ReadRecord<PublishedVote>(scenario, second, vote.RecordId);
        Assert.Equal((voteRecord.Totals!.Yea, voteRecord.Totals.Nay), (vote.YeaCount, vote.NayCount));
        Assert.Equal(voteRecord.ToEntry().Alignment, vote.Alignment);
        Assert.NotNull(vote.Alignment);
        var action = after.Single(entry => entry.Kind == PublishedRecordKind.Action);
        Assert.Equal(["chair"], action.CosponsorOfficials!.Select(official => official.Id));
        Assert.All(after.Where(entry => entry.Kind == PublishedRecordKind.Statement), entry => Assert.Null(entry.CosponsorOfficials));
        foreach (var entry in after)
        {
            Assert.DoesNotContain(PublicationProtocol.RecordDataPath(entry.RecordId), scenario.Releases.WriteOrder);
            Assert.Contains((first.DirectoryName, PublicationProtocol.RecordDataPath(entry.RecordId)), scenario.Releases.Links);
        }
    }

    [Fact]
    public async Task TheRewriteOptionWithNoChangeToAnyRecordIsStillAPublishSoTheOperatorCanRunItAlone()
    {
        var scenario = new PublicationScenario();
        await PublishFirstAsync(scenario);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteEntries = true });

        Assert.Equal(2, second.ReleaseNumber);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3")));
    }

    [Fact]
    public async Task TheRewriteOptionDoesNotBringBackAWithdrawnRecordBecauseOnlyCarriedEntriesAreRebuilt()
    {
        var scenario = new PublicationScenario();
        var kept = scenario.AddAction('a');
        var wrong = scenario.AddAction('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Action, kept), new PublishedRecordReference(PublishedRecordKind.Action, wrong)));
        scenario.Publications.ReviewStatuses[new(PublishedRecordKind.Action, wrong)] = ReviewStatus.ApprovalWithdrawn;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Action, wrong, "Wrong.")] });

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { RewriteEntries = true });

        var manifest = Manifest(scenario, third);
        Assert.Equal(kept, Assert.Single(manifest.Records).RecordId);
        Assert.Equal(wrong, Assert.Single(manifest.Withdrawals).RecordId);
    }

    [Fact]
    public async Task TheRewriteOptionIsInThePayloadHashSoTheSameKeyIsAMismatchWithoutIt()
    {
        var scenario = new PublicationScenario();
        await PublishFirstAsync(scenario);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteEntries = true });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2")));
    }

    [Fact]
    public async Task ACosponsorCountsInTheSummaryOfAnOfficialWhoIsNotTouchedAndNeverAsASponsoredBill()
    {
        var scenario = new PublicationScenario();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('a', speakerIds: ["chair"]))));
        scenario.OfficialPages.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('b'))));

        var summaries = Summaries(scenario, second);
        Assert.Equal(1, summaries["chair"].CosponsoredBills);
        Assert.Equal(0, summaries["mayor"].CosponsoredBills);
        Assert.DoesNotContain(scenario.OfficialPages, page => page.Profile.Id == "chair");
        var chairEntries = Manifest(scenario, second).Records.Where(entry => entry.Officials.Any(official => official.Id == "chair"));
        Assert.DoesNotContain(chairEntries, entry => entry.Kind == PublishedRecordKind.Action);
    }

    [Fact]
    public async Task AnUntouchedOfficialKeepsTheVoteFiguresOfThePreviousSummaryAndATouchedOfficialGetsNewOnes()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a')),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b', speakerIds: ["chair"]))));
        var mayor = Summaries(scenario, first)["mayor"];
        Assert.Equal(119, mayor.Congress);
        Assert.Equal((1, 0), (mayor.Attendance.Single().Held, mayor.Attendance.Single().Missed));

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('c', speakerIds: ["chair"]))));

        Assert.Equal(Json(mayor), Json(Summaries(scenario, second)["mayor"]));
        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('d'))));
        Assert.Equal(2, Summaries(scenario, third)["mayor"].Attendance.Single().Held);
    }

    [Fact]
    public async Task AReleaseWithNoSummaryFileGivesAnUntouchedOfficialNoVoteDataUntilARewriteOfAllOfficialsBecauseNoReaderGuessesADefault()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a')),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b', speakerIds: ["chair"]))));
        AsReleaseFromBeforeTheNewFields(scenario, first);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('c', speakerIds: ["chair"]))));
        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { RewriteOfficials = true });

        var untouched = Summaries(scenario, second)["mayor"];
        Assert.Null(untouched.Congress);
        Assert.Empty(untouched.Attendance);
        Assert.Equal(119, Summaries(scenario, third)["mayor"].Congress);
    }

    private static async Task<PublicationReleaseSummary> PublishFirstAsync(PublicationScenario scenario) =>
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a')),
            new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('b')),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('c'))));

    private static void AsReleaseFromBeforeTheNewFields(PublicationScenario scenario, PublicationReleaseSummary release)
    {
        var files = scenario.Releases.Directories[release.DirectoryName];
        var manifest = Manifest(scenario, release);
        files[PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(
            manifest with { Records = [.. manifest.Records.Select(WithoutNewFields)] }, PublicationProtocol.JsonOptions);
        files.Remove(PublicationProtocol.OfficialSummariesPath);
    }

    private static PublishedRecordEntry WithoutNewFields(PublishedRecordEntry entry) =>
        entry with { YeaCount = null, NayCount = null, Alignment = null, CosponsorOfficials = null };

    private static PublicationRelease Manifest(PublicationScenario scenario, PublicationReleaseSummary release) =>
        JsonSerializer.Deserialize<PublicationRelease>(scenario.Releases.Directories[release.DirectoryName][PublicationProtocol.ManifestPath],
            PublicationProtocol.JsonOptions)!;

    private static Dictionary<string, PublishedOfficialSummary> Summaries(PublicationScenario scenario, PublicationReleaseSummary release)
    {
        var file = JsonSerializer.Deserialize<PublishedOfficialSummaries>(
            scenario.Releases.Directories[release.DirectoryName][PublicationProtocol.OfficialSummariesPath], PublicationProtocol.JsonOptions)!;
        file.Validate();
        return file.Officials.ToDictionary(row => row.Id);
    }

    private static TRecord ReadRecord<TRecord>(PublicationScenario scenario, PublicationReleaseSummary release, string recordId) =>
        JsonSerializer.Deserialize<TRecord>(scenario.Releases.Directories[release.DirectoryName][PublicationProtocol.RecordDataPath(recordId)],
            PublicationProtocol.JsonOptions)!;

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, PublicationProtocol.JsonOptions);
}
