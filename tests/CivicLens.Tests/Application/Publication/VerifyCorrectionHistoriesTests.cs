using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.PublicApi;

namespace CivicLens.Tests.Application.Publication;

public sealed class VerifyCorrectionHistoriesTests
{
    [Fact]
    public async Task RequiresOwnerBeforeReadingReleaseRecords()
    {
        var scenario = new PublicationScenario();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Reviewer, 1, null));

        Assert.Empty(scenario.Releases.ReadPaths);
    }

    [Fact]
    public async Task VerifiesAlreadyBoundEmptyHistoryAndContinuesWithinLimit()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('a', "Headline", official: "mayor");
        var release = Release(1);
        Add(scenario, release, sample);
        var binding = new PublishedRevisionBinding(sample.Entry.Kind, sample.Entry.RecordId, sample.Entry.RevisionNumber);
        var historyId = PublishedCorrectionHistoryIdentity.Of([]);
        scenario.Publications.CorrectionHistories[1] = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>
        {
            [new(sample.Entry.Kind, sample.Entry.RecordId)] = new(new(binding, historyId), [])
        };
        var next = PublishedSamples.Statement('b', "Second", official: "mayor");
        Add(scenario, Release(2), next);
        scenario.Publications.CorrectionHistories[2] = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>
        {
            [new(next.Entry.Kind, next.Entry.RecordId)] = new(new(new(next.Entry.Kind, next.Entry.RecordId, next.Entry.RevisionNumber),
                PublishedCorrectionHistoryIdentity.Of([])), [])
        };

        var result = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal((1, 1, 0), (result.Examined, result.Verified, result.Failed));
        Assert.NotNull(result.NextCursor);
        Assert.Single(scenario.Releases.ReadPaths);
    }

    [Fact]
    public async Task FailedFirstFileReportsWithinBoundAndCursorContinuesToValidLaterRecord()
    {
        var scenario = new PublicationScenario();
        var missing = PublishedSamples.Statement('a', "Missing", official: "mayor");
        var valid = PublishedSamples.Statement('b', "Valid", official: "mayor");
        var releaseOne = Release(11);
        var releaseTwo = Release(12);
        scenario.Publications.CorrectionBootstrapCandidates.Add(new(releaseOne, new(missing.Entry.Kind, missing.Entry.RecordId,
            missing.Entry.RevisionNumber, missing.Entry.FirstPublishedAtUtc, Facts(missing.Entry))));
        Add(scenario, releaseTwo, valid);
        scenario.Publications.CorrectionHistories[12] = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>
        {
            [new(valid.Entry.Kind, valid.Entry.RecordId)] = History(valid.Entry, [])
        };
        var verifier = new VerifyCorrectionHistories(scenario.Publications, scenario.Releases);

        var first = await verifier.ExecuteAsync(PublicationScenario.Owner, 1, null);
        var resumed = await verifier.ExecuteAsync(PublicationScenario.Owner, 1, CorrectionHistoryVerificationCursor.Parse(first.NextCursor));

        Assert.Equal((1, 1, "recordMissing"), (first.Examined, first.Failed, Assert.Single(first.Failures).Reason));
        Assert.NotNull(first.NextCursor);
        Assert.Equal((1, 1, 0), (resumed.Examined, resumed.Verified, resumed.Failed));
    }

    [Fact]
    public async Task MissingBindingFailsWithoutWriting()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('c', "Headline", official: "mayor");
        Add(scenario, Release(3), sample);
        scenario.Publications.CorrectionHistories[3] = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>();

        var result = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.Equal((1, 0, 1, "recordInvalid"), (result.Examined, result.Verified, result.Failed, Assert.Single(result.Failures).Reason));
        Assert.Empty(scenario.Publications.PreparedCorrectionHistories);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task FileRevisionMismatchFailsEvenWhenAHistoryIsBound()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('f', "Headline", official: "mayor");
        Add(scenario, Release(6), sample);
        var changed = scenario.Publications.CorrectionBootstrapCandidates[0];
        scenario.Publications.CorrectionBootstrapCandidates[0] = changed with { Record = changed.Record with { RevisionNumber = 2 } };
        var binding = new PublishedRevisionBinding(sample.Entry.Kind, sample.Entry.RecordId, 2);
        scenario.Publications.CorrectionHistories[6] = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>
        {
            [new(sample.Entry.Kind, sample.Entry.RecordId)] = new(new(binding, PublishedCorrectionHistoryIdentity.Of([])), [])
        };

        var result = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal((1, 0, 1), (result.Examined, result.Verified, result.Failed));
    }

    [Fact]
    public async Task BindingRevisionMismatchFailsWhenFileAndStoredFactsAgree()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('c', "Headline", official: "mayor", revision: 2);
        Add(scenario, Release(14), sample);
        var wrongRevision = new PublishedRevisionBinding(sample.Entry.Kind, sample.Entry.RecordId, 1);
        scenario.Publications.CorrectionHistories[14] = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>
        {
            [new(sample.Entry.Kind, sample.Entry.RecordId)] = new(new(wrongRevision, PublishedCorrectionHistoryIdentity.Of([])), [])
        };

        var result = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal((1, 0, 1), (result.Examined, result.Verified, result.Failed));
    }

    [Theory]
    [InlineData("firstPublished")]
    [InlineData("facts")]
    public async Task MissingStoredPublicationDataIsNotSubstitutedWithAZeroValue(string field)
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('d', "Headline", official: "mayor");
        Add(scenario, Release(13), sample);
        var candidate = scenario.Publications.CorrectionBootstrapCandidates[0];
        scenario.Publications.CorrectionBootstrapCandidates[0] = candidate with
        {
            Record = candidate.Record with
            {
                FirstPublishedAtUtc = field == "firstPublished" ? null : candidate.Record.FirstPublishedAtUtc,
                Facts = field == "facts" ? null : candidate.Record.Facts
            }
        };
        scenario.Publications.CorrectionHistories[13] = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>
        {
            [new(sample.Entry.Kind, sample.Entry.RecordId)] = History(sample.Entry, [])
        };

        var result = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal((0, 1), (result.Verified, result.Failed));
    }

    [Fact]
    public async Task MismatchedTypedCorrectionContentFails()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('a', "Headline", official: "mayor", revision: 2);
        var fileCorrections = new[] { new PublishedCorrection { ReplacedRevisionNumber = 1, PublishedAtUtc = DateTimeOffset.UnixEpoch, Note = "File note" } };
        var body = (PublishedStatement)sample.Body with { Corrections = fileCorrections };
        sample = sample with { Entry = body.ToEntry(), Body = body };
        Add(scenario, Release(7), sample);
        var record = new PublishedRevisionBinding(sample.Entry.Kind, sample.Entry.RecordId, sample.Entry.RevisionNumber);
        var storedCorrections = new[] { new PublishedCorrection { ReplacedRevisionNumber = 1, PublishedAtUtc = DateTimeOffset.UnixEpoch, Note = "Different valid note" } };
        scenario.Publications.CorrectionHistories[7] = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>
        {
            [new(sample.Entry.Kind, sample.Entry.RecordId)] = new(new(record, PublishedCorrectionHistoryIdentity.Of(storedCorrections)), storedCorrections)
        };

        var result = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal((0, 1), (result.Verified, result.Failed));
    }

    [Fact]
    public async Task VerifiesOrderedNonemptyHistoriesForMixedRecordKindsWithOriginalOffsetsAndTicks()
    {
        var scenario = new PublicationScenario();
        var corrections = new[]
        {
            new PublishedCorrection
            {
                ReplacedRevisionNumber = 3,
                PublishedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.FromHours(5)).AddTicks(31),
                Note = "First correction"
            },
            new PublishedCorrection
            {
                ReplacedRevisionNumber = 1,
                PublishedAtUtc = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.FromHours(-4)).AddTicks(47),
                Note = "Earlier correction"
            }
        };
        var statement = PublishedSamples.Statement('e', "Statement", official: "mayor", revision: 4);
        var statementBody = (PublishedStatement)statement.Body with { Corrections = corrections };
        statement = statement with { Entry = statementBody.ToEntry(), Body = statementBody };
        var action = PublishedSamples.Action("Action", "Summary", "Tracked bill", "Official source text.");
        var actionBody = (PublishedAction)action.Body with { RevisionNumber = 4, Corrections = corrections };
        action = action with { Entry = actionBody.ToEntry(), Body = actionBody };
        var release = Release(10);
        Add(scenario, release, statement);
        Add(scenario, release, action);
        scenario.Publications.CorrectionHistories[10] = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>
        {
            [new(statement.Entry.Kind, statement.Entry.RecordId)] = History(statement.Entry, corrections),
            [new(action.Entry.Kind, action.Entry.RecordId)] = History(action.Entry, corrections)
        };

        var result = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 2, null);

        Assert.Equal((2, 2, 0), (result.Examined, result.Verified, result.Failed));
    }

    [Fact]
    public async Task ChangedSelectionRejectsContinuation()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('d', "Headline", official: "mayor");
        Add(scenario, Release(4), sample);
        var second = PublishedSamples.Statement('e', "Second", official: "mayor");
        Add(scenario, Release(4), second);
        var first = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, null);
        scenario.Publications.CorrectionBootstrapCandidates.Add(scenario.Publications.CorrectionBootstrapCandidates[0] with
        {
            Release = Release(5)
        });

        var resumed = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, CorrectionHistoryVerificationCursor.Parse(first.NextCursor));

        Assert.True(resumed.StaleCursor);
        Assert.Null(resumed.NextCursor);
    }

    [Fact]
    public async Task ChangedSelectionAfterLastFileBatchMakesTerminalResultStale()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('b', "Headline", official: "mayor");
        Add(scenario, Release(8), sample);
        scenario.Publications.BeforeReadingVerificationFingerprint = () =>
            scenario.Publications.CorrectionBootstrapCandidates.Add(scenario.Publications.CorrectionBootstrapCandidates[0] with { Release = Release(9) });

        var result = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.True(result.StaleCursor);
        Assert.Null(result.NextCursor);
        Assert.NotEmpty(result.SelectionFingerprint);
    }

    [Fact]
    public async Task FailureReportCapsDetailsWithoutLosingTheTotalOrSkippingFiles()
    {
        var scenario = new PublicationScenario();
        var sample = PublishedSamples.Statement('a', "Missing file", official: "mayor");
        var release = Release(15);
        for (var number = 1; number <= 51; number++)
            scenario.Publications.CorrectionBootstrapCandidates.Add(new(release,
                new(sample.Entry.Kind, number.ToString("x64"), sample.Entry.RevisionNumber,
                    sample.Entry.FirstPublishedAtUtc, Facts(sample.Entry))));

        var result = await new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 51, null);

        Assert.Equal((51, 0, 51, 50), (result.Examined, result.Verified, result.Failed, result.Failures.Count));
        Assert.Equal(51, scenario.Releases.ReadPaths.Count);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task CancellationPropagatesThroughTheStoreBoundary()
    {
        var scenario = new PublicationScenario();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VerifyCorrectionHistories(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, 1, null, cancellation.Token));
    }

    private static void Add(PublicationScenario scenario, PublicationReleaseSummary release, SampleRecord sample)
    {
        scenario.Publications.CorrectionBootstrapCandidates.Add(new(release, new(sample.Entry.Kind, sample.Entry.RecordId,
            sample.Entry.RevisionNumber, sample.Entry.FirstPublishedAtUtc, Facts(sample.Entry))));
        if (!scenario.Releases.Directories.TryGetValue(release.DirectoryName, out var files))
        {
            files = [];
            scenario.Releases.Directories.Add(release.DirectoryName, files);
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(sample.Body, sample.Body.GetType(), PublicationProtocol.JsonOptions);
        // Negative cases must fail on changed stored data, not malformed file fixtures.
        switch (sample.Body)
        {
            case PublishedStatement statement: statement.Validate(); break;
            case PublishedAction action: action.Validate(); break;
            default: throw new InvalidOperationException("The fixture needs a supported published record type.");
        }
        files[PublicationProtocol.RecordDataPath(sample.Entry.RecordId)] = json;
    }

    private static PublishedEntryFacts Facts(PublishedRecordEntry entry) => new(entry.Headline, entry.SourceHost, entry.EventDate,
        entry.BillTitle, entry.LinkType, entry.StatementRecordId, entry.TargetKind, entry.TargetRecordId, entry.DocumentType,
        [.. entry.Officials.Select(official => official.Id)], entry.MatchedOfficialId, entry.IssueIds, entry.PolicyAreaIds, entry.BillLine, entry.ApprovedBy);

    private static StoredCorrectionHistory History(PublishedRecordEntry entry, PublishedCorrection[] corrections)
    {
        var record = new PublishedRevisionBinding(entry.Kind, entry.RecordId, entry.RevisionNumber);
        return new(new(record, PublishedCorrectionHistoryIdentity.Of(corrections)), corrections);
    }

    private static PublicationReleaseSummary Release(int number) => new(number, $"{number:D6}-{new string('a', 32)}",
        DateTimeOffset.UnixEpoch.AddDays(number), 1);
}
