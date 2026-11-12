using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class BackfillStoredColumnsTests
{
    [Fact]
    public async Task StoredColumnsKeepCompleteTalliesAndBackfillRejectsAFileForAnotherRecordWithTheSameRevision()
    {
        var expectedScenario = new PublicationScenario();
        var expectedId = expectedScenario.AddVote('d');
        var expectedRelease = await PublishVoteAsync(expectedScenario, expectedId, "expected");
        var expectedVote = ReadVote(expectedScenario, expectedRelease, expectedId);
        var columns = VoteStoredColumns.From(expectedVote);
        Assert.False(columns.PartyTallies.IsDefault);
        Assert.Equal(expectedVote.PartyTallies, columns.PartyTallies);
        Assert.True(VoteStoredColumns.From(expectedVote with { PartyTallies = null }).PartyTallies.IsDefault);

        var otherScenario = new PublicationScenario();
        var otherId = otherScenario.AddVote('e', totalLines:
            [PublicationScenario.DefaultPartyTotal, "Party total R: Yea 0, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: X1, A, R, AL, Nay"]);
        var otherRelease = await PublishVoteAsync(otherScenario, otherId, "other");
        var wrongRecordFile = otherScenario.Releases.Directories[otherRelease.DirectoryName][PublicationProtocol.RecordDataPath(otherId)];
        var wrongVote = JsonSerializer.Deserialize<PublishedVote>(wrongRecordFile, PublicationProtocol.JsonOptions)!;
        Assert.Equal(1, wrongVote.RevisionNumber);
        Assert.Equal(otherId, wrongVote.RecordId);
        Assert.Equal(2, wrongVote.PartyTallies!.Length);
        expectedScenario.Releases.Directories[expectedRelease.DirectoryName][PublicationProtocol.RecordDataPath(expectedId)] = wrongRecordFile;
        expectedScenario.Publications.VoteColumnGaps.Add(new(expectedId, expectedVote.RevisionNumber, new string('a', 64), new(1, null)));

        var result = await new BackfillStoredColumns(expectedScenario.Publications, expectedScenario.Releases, expectedScenario.Votes).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(1, result.OpenVoteRevisionsWithoutKeptFile);
        Assert.Empty(expectedScenario.Publications.VoteColumnWrites.SelectMany(write => write));
    }

    private static Task<PublicationReleaseSummary> PublishVoteAsync(PublicationScenario scenario, string recordId, string key) =>
        scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request(key, new PublishedRecordReference(PublishedRecordKind.Vote, recordId)));

    private static PublishedVote ReadVote(PublicationScenario scenario, PublicationReleaseSummary release, string recordId) =>
        JsonSerializer.Deserialize<PublishedVote>(scenario.Releases.Directories[release.DirectoryName][PublicationProtocol.RecordDataPath(recordId)],
            PublicationProtocol.JsonOptions)!;
}
