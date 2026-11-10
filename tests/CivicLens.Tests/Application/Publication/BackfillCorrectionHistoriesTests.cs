using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.PublicApi;

namespace CivicLens.Tests.Application.Publication;

public sealed class BackfillCorrectionHistoriesTests
{
    [Fact]
    public async Task ExaminedLimitIncludesMissingFilesAndCursorContinuesWhileFreshScanRetriesThem()
    {
        var scenario = new PublicationScenario();
        scenario.Publications.CorrectionBootstrapCandidates.Add(Candidate(1, 'a'));
        scenario.Publications.CorrectionBootstrapCandidates.Add(Candidate(2, 'b'));
        var backfill = new BackfillCorrectionHistories(scenario.Publications, scenario.Releases);

        var first = await backfill.ExecuteAsync(PublicationScenario.Owner, 1, null);
        var resumed = await backfill.ExecuteAsync(PublicationScenario.Owner, 1, CorrectionHistoryBootstrapCursor.Parse(first.NextCursor));
        var fresh = await backfill.ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal((1, 1, "recordMissing"), (first.Examined, first.Failed, Assert.Single(first.Failures).Reason));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(2, resumed.Failures.Single().ReleaseNumber);
        Assert.Equal(1, fresh.Failures.Single().ReleaseNumber);
    }

    [Fact]
    public async Task MissingFileDoesNotPreventLaterValidEmptyHistoryFromBeingBound()
    {
        var scenario = new PublicationScenario();
        scenario.Publications.CorrectionBootstrapCandidates.Add(Candidate(1, 'a'));
        var sample = PublishedSamples.Statement('b', "Headline", official: "mayor");
        var summary = new PublicationReleaseSummary(1, "000001-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", DateTimeOffset.UnixEpoch, 1);
        scenario.Releases.Directories[summary.DirectoryName] = new Dictionary<string, byte[]>
        {
            [PublicationProtocol.RecordDataPath(sample.Entry.RecordId)] = JsonSerializer.SerializeToUtf8Bytes(sample.Body,
                sample.Body.GetType(), PublicationProtocol.JsonOptions)
        };
        scenario.Publications.CorrectionBootstrapCandidates.Add(new(summary, StoredRecord(sample.Entry)));

        var result = await new BackfillCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 2, null);

        Assert.Equal((2, 1, 1), (result.Examined, result.Failed, result.Added));
        var history = Assert.Single(scenario.Publications.PreparedCorrectionHistories);
        Assert.Empty(history.Corrections);
        Assert.Equal(sample.Entry.RecordId, history.Record.RecordId);
    }

    [Fact]
    public async Task RecordWhoseIdentityDoesNotMatchStoredRevisionIsReportedAndNeverPrepared()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('c', "Headline", official: "mayor");
        var summary = new PublicationReleaseSummary(1, "000001-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", DateTimeOffset.UnixEpoch, 1);
        scenario.Releases.Directories[summary.DirectoryName] = new Dictionary<string, byte[]>
        {
            [PublicationProtocol.RecordDataPath(sample.Entry.RecordId)] = JsonSerializer.SerializeToUtf8Bytes(sample.Body,
                sample.Body.GetType(), PublicationProtocol.JsonOptions)
        };
        scenario.Publications.CorrectionBootstrapCandidates.Add(new(summary, StoredRecord(sample.Entry with { RevisionNumber = 2 })));

        var result = await new BackfillCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal((1, 1, "recordInvalid"), (result.Examined, result.Failed, Assert.Single(result.Failures).Reason));
        Assert.Empty(scenario.Publications.PreparedCorrectionHistories);
    }

    [Fact]
    public async Task LargeRequestedLimitStillReadsAndPreparesCandidatesInSmallPages()
    {
        var scenario = new PublicationScenario();
        for (var release = 1; release <= 51; release++)
            scenario.Publications.CorrectionBootstrapCandidates.Add(Candidate(release, 'a'));

        var result = await new BackfillCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 51, null);

        Assert.Equal(51, result.Examined);
        Assert.Equal(51, result.Failed);
        Assert.Equal(50, result.Failures.Count);
        Assert.Equal(3, scenario.Publications.CorrectionBootstrapPageReads);
    }

    [Theory]
    [InlineData("permission", "recordUnreadable")]
    [InlineData("io", "recordUnreadable")]
    [InlineData("directory", "recordMissing")]
    public async Task UnreadableFilesAreReportedWhileLaterValidFilesContinue(string failure, string reason)
    {
        var scenario = new PublicationScenario();
        var broken = Candidate(1, 'a');
        scenario.Publications.CorrectionBootstrapCandidates.Add(broken);
        scenario.Releases.ReadFailures[PublicationProtocol.RecordDataPath(broken.Record.RecordId)] = failure switch
        {
            "permission" => new UnauthorizedAccessException("Test file permissions"),
            "directory" => new DirectoryNotFoundException("Test missing directory"),
            _ => new IOException("Test read failure")
        };
        var sample = PublishedSamples.Statement('b', "Headline", official: "mayor");
        scenario.Publications.CorrectionBootstrapCandidates.Add(new(broken.Release, StoredRecord(sample.Entry)));
        Save(scenario, broken.Release, sample);

        var result = await new BackfillCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 2, null);

        Assert.Equal((2, 1, 1), (result.Examined, result.Failed, result.Added));
        Assert.Equal(reason, Assert.Single(result.Failures).Reason);
        Assert.Equal(sample.Entry.RecordId, Assert.Single(scenario.Publications.PreparedCorrectionHistories).Record.RecordId);
    }

    [Fact]
    public async Task FreshScanBindsAnEarlierFileAfterItIsRestored()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('b', "Headline", official: "mayor");
        var release = Candidate(1, 'a').Release;
        scenario.Publications.CorrectionBootstrapCandidates.Add(new(release, StoredRecord(sample.Entry)));
        var backfill = new BackfillCorrectionHistories(scenario.Publications, scenario.Releases);
        var missing = await backfill.ExecuteAsync(PublicationScenario.Owner, 1, null);
        Save(scenario, release, sample);

        var restored = await backfill.ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal(1, missing.Failed);
        Assert.Equal((1, 1, 0), (restored.Examined, restored.Added, restored.Failed));
    }

    [Fact]
    public async Task FileWhoseFactsDifferFromItsStoredRevisionCannotSupplyAHistory()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('b', "Changed headline", official: "mayor");
        var release = Candidate(1, 'a').Release;
        var stored = StoredRecord(sample.Entry);
        scenario.Publications.CorrectionBootstrapCandidates.Add(new(release, stored with { Facts = stored.Facts! with { Headline = "Published headline" } }));
        Save(scenario, release, sample);

        var result = await new BackfillCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal((1, 0, 1), (result.Examined, result.Added, result.Failed));
        Assert.Empty(scenario.Publications.PreparedCorrectionHistories);
    }

    private static void Save(PublicationScenario scenario, PublicationReleaseSummary release, SampleRecord sample) =>
        scenario.Releases.Directories[release.DirectoryName] = new Dictionary<string, byte[]>
        {
            [PublicationProtocol.RecordDataPath(sample.Entry.RecordId)] = JsonSerializer.SerializeToUtf8Bytes(sample.Body,
                sample.Body.GetType(), PublicationProtocol.JsonOptions)
        };

    private static CorrectionHistoryBootstrapCandidate Candidate(int releaseNumber, char id)
    {
        var record = new StoredReleaseRecord(PublishedRecordKind.Action, new string(id, 64), 1,
            DateTimeOffset.UnixEpoch, new PublishedEntryFacts("Headline", null, null, null, null, null, null, null, null,
                [], null, [], [], null));
        return new(new PublicationReleaseSummary(releaseNumber, $"{releaseNumber:D6}-{new string('a', 32)}",
            DateTimeOffset.UnixEpoch.AddDays(releaseNumber), 1), record);
    }

    private static StoredReleaseRecord StoredRecord(PublishedRecordEntry entry) => new(entry.Kind, entry.RecordId, entry.RevisionNumber,
        entry.FirstPublishedAtUtc, new PublishedEntryFacts(entry.Headline, entry.SourceHost, entry.EventDate, entry.BillTitle,
            entry.LinkType, entry.StatementRecordId, entry.TargetKind, entry.TargetRecordId, entry.DocumentType,
            [.. entry.Officials.Select(official => official.Id)], entry.MatchedOfficialId, entry.IssueIds, entry.PolicyAreaIds,
            entry.BillLine, entry.ApprovedBy));
}
