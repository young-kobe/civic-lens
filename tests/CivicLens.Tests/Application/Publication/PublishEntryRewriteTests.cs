using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
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
        Assert.All(carried, entry => Assert.Equal((null, null, null, null), (entry.YeaCount, entry.NayCount, entry.Alignment, entry.CosponsorIds)));
    }

    [Fact]
    public async Task TheHomeAndAllRecordsPagesFollowTheEventDayThenTheFirstPublicationDay()
    {
        var scenario = new PublicationScenario();
        var newerEventPublishedEarlier = scenario.AddStatement('c', statementDate: new DateOnly(2026, 1, 20));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k1", new PublishedRecordReference(PublishedRecordKind.Statement, newerEventPublishedEarlier)));
        scenario.Clock.Now = scenario.Clock.Now.AddDays(1);
        var olderEventPublishedLater = scenario.AddStatement('d', statementDate: new DateOnly(2025, 5, 5));
        var undated = scenario.AddStatement('e');

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Statement, olderEventPublishedLater),
            new PublishedRecordReference(PublishedRecordKind.Statement, undated)));

        var records = Manifest(scenario, second).Records;
        Assert.Equal([undated, newerEventPublishedEarlier, olderEventPublishedLater], records.Select(entry => entry.RecordId));
        Assert.Null(records[0].EventDate);
        Assert.Equal(new DateOnly(2026, 2, 2), ReleaseRecordOrder.Day(records[0]));
        Assert.Equal(records.Select(entry => entry.RecordId), ReleaseRecordOrder.NewestFirst(records, ReleaseRecordOrder.Day, entry => entry.FirstPublishedAtUtc, entry => entry.RecordId).Select(entry => entry.RecordId));
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
        Assert.Equal(PublishedVoteAlignment.Split, vote.Alignment);
        var action = after.Single(entry => entry.Kind == PublishedRecordKind.Action);
        Assert.Equal(["chair"], action.CosponsorIds!);
        Assert.All(after.Where(entry => entry.Kind == PublishedRecordKind.Statement), entry => Assert.Null(entry.CosponsorIds));
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
            new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('b', cosponsorWithdrawn: false))));

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
    public async Task ThePublishHandsTheHomeCardsTheVoteCountsAndTheLeaderOfTheCarriedSummaryAndARewriteOfAllOfficialsRecomputesThem()
    {
        var scenario = new PublicationScenario();
        scenario.Clock.Now = new(2025, 4, 12, 0, 0, 0, TimeSpan.Zero);
        var first = await PublishFirstAsync(scenario);
        var vote = Assert.IsType<PublishedRecordEntry>(scenario.Highlights!.ClosestVote);
        Assert.Equal((1, 1, PublishedVoteAlignment.Split), (vote.YeaCount, vote.NayCount, vote.Alignment));
        Assert.Empty(scenario.Highlights.BreakLeaders);
        var carried = Summaries(scenario, first);
        var withLeader = carried["mayor"] with
        {
            PartyVotes = [new() { Chamber = PublishedChamber.House, Party = PublishedPartyMajority.Democrat, SplitVotesCast = 40, Breaks = 12 }]
        };
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.OfficialSummariesPath] = JsonSerializer.SerializeToUtf8Bytes(
            new PublishedOfficialSummaries { Officials = [.. carried.Values.Select(row => row.Id == "mayor" ? withLeader : row)] }, PublicationProtocol.JsonOptions);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('d', speakerIds: ["chair"]))));

        var leader = Assert.Single(scenario.Highlights!.BreakLeaders);
        Assert.Equal(("mayor", 40, 12), (leader.Official.Profile.Id, leader.Cast, leader.Breaks));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { RewriteOfficials = true });
        Assert.Empty(scenario.Highlights.BreakLeaders);
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

    [Fact]
    public async Task TheCosponsorCountIsNullWhileAnActionEntryHasNoCosponsorDataAndARealCountIncludingZeroAfterTheRewrite()
    {
        var scenario = new PublicationScenario();
        var first = await PublishFirstAsync(scenario);
        AsReleaseFromBeforeTheNewFields(scenario, first);

        var partial = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('d', speakerIds: ["chair"]))));
        var rewritten = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { RewriteEntries = true });

        Assert.All(Summaries(scenario, partial).Values, row => Assert.Null(row.CosponsoredBills));
        var complete = Summaries(scenario, rewritten);
        Assert.Equal(1, complete["chair"].CosponsoredBills);
        Assert.Equal(0, complete["mayor"].CosponsoredBills);
    }

    [Fact]
    public async Task AReleaseWithNoActionCountsZeroCosponsoredBillsBecauseNoActionEntryLacksData()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('a', speakerIds: ["chair"]))));

        Assert.Equal(0, Summaries(scenario, first)["chair"].CosponsoredBills);
    }

    [Fact]
    public async Task TheRewriteStopsOnACarriedEntryThatDiffersFromItsRecordFileOutsideTheNewFieldsSoItNeverHidesACorruptRelease()
    {
        var scenario = new PublicationScenario();
        var first = await PublishFirstAsync(scenario);
        var manifest = Manifest(scenario, first);
        var action = manifest.Records.Single(entry => entry.Kind == PublishedRecordKind.Action);
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(
            manifest with { Records = [.. manifest.Records.Select(entry => entry == action ? entry with { EventDate = new DateOnly(2020, 1, 1) } : entry)] },
            PublicationProtocol.JsonOptions);

        var failure = await Assert.ThrowsAsync<ReleaseRewriteException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteEntries = true }));

        Assert.Contains(action.RecordId, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Action", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task TheRewriteNamesTheRecordWhenItsFileIsNotAValidRecordSoTheOperatorKnowsWhichFileToCheck(string content)
    {
        var scenario = new PublicationScenario();
        var first = await PublishFirstAsync(scenario);
        var action = Manifest(scenario, first).Records.Single(entry => entry.Kind == PublishedRecordKind.Action);
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.RecordDataPath(action.RecordId)] = System.Text.Encoding.UTF8.GetBytes(content);

        var failure = await Assert.ThrowsAsync<ReleaseRewriteException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteEntries = true }));

        Assert.Contains(action.RecordId, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Action", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOldActionEntryAndANewActionEntryTogetherGiveNoCountToAnyOfficialBecauseOnePartialEntryMakesEveryCountPartial()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('a', cosponsorWithdrawn: false)),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('c', speakerIds: ["chair"]))));
        AsReleaseFromBeforeTheNewFields(scenario, first);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('b', cosponsorWithdrawn: false))));

        var entries = Manifest(scenario, second).Records.Where(entry => entry.Kind == PublishedRecordKind.Action).ToArray();
        Assert.Equal([false, true], entries.Select(entry => entry.CosponsorIds is null).Order());
        Assert.All(Summaries(scenario, second).Values, row => Assert.Null(row.CosponsoredBills));
    }

    [Fact]
    public async Task TheRewriteOfACorrectedRecordKeepsTheRevisionOfTheEntryBecauseTheRecordFileIsTheCorrectedOne()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', cosponsorWithdrawn: false);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k1", new PublishedRecordReference(PublishedRecordKind.Action, action)));
        scenario.ReviseAction(action, 2, correctionNote: "Fixed the issue.");
        var corrected = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", new PublishedRecordReference(PublishedRecordKind.Action, action)));
        AsReleaseFromBeforeTheNewFields(scenario, corrected);

        var rewritten = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { RewriteEntries = true });

        var entry = Assert.Single(Manifest(scenario, rewritten).Records);
        Assert.Equal(2, entry.RevisionNumber);
        Assert.Equal(["chair"], entry.CosponsorIds!);
    }

    private static async Task<PublicationReleaseSummary> PublishFirstAsync(PublicationScenario scenario)
    {
        scenario.RosterMembers = [Member("M000001", "Democrat"), Member("R1", "Republican")];
        return await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote,
                scenario.AddVote('a', totalLines: [PublicationScenario.DefaultPartyTotal, "Party total R: Yea 0, Nay 1, Present 0, Not voting 0"],
                    otherMemberLines: ["Member: R1, A, R, AL, No"])),
            new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('b', cosponsorWithdrawn: false)),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('c'))));
    }

    private static RosterMember Member(string id, string party) =>
        new(id, id, null, null, null, [new(0, "rep", new(2025, 1, 3), new(2027, 1, 3), "MA", 1, null, party, null)]);

    private static void AsReleaseFromBeforeTheNewFields(PublicationScenario scenario, PublicationReleaseSummary release)
    {
        var files = scenario.Releases.Directories[release.DirectoryName];
        var manifest = Manifest(scenario, release);
        files[PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(
            manifest with { Records = [.. manifest.Records.Select(WithoutNewFields)] }, PublicationProtocol.JsonOptions);
        files.Remove(PublicationProtocol.OfficialSummariesPath);
    }

    private static PublishedRecordEntry WithoutNewFields(PublishedRecordEntry entry) =>
        entry with { YeaCount = null, NayCount = null, Alignment = null, CosponsorIds = null };

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
