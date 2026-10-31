using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublicationReleaseTests
{
    private static readonly string ChangeId = new('a', 32);
    private static readonly string StatementId = new('b', 64);

    [Fact]
    public void KindIsWrittenAsALowercaseCamelStringSoReadersNeverSeeANumber()
    {
        var json = JsonSerializer.Serialize(Entry(PublishedRecordKind.DocumentChange, ChangeId), PublicationProtocol.JsonOptions);

        Assert.Contains("\"kind\":\"documentChange\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"statement\"",
            JsonSerializer.Serialize(Entry(PublishedRecordKind.Statement, StatementId), PublicationProtocol.JsonOptions), StringComparison.Ordinal);
    }

    [Fact]
    public void KindRoundTripsAndRefusesANumber()
    {
        var entry = Entry(PublishedRecordKind.Statement, StatementId);
        var json = JsonSerializer.Serialize(entry, PublicationProtocol.JsonOptions);

        Assert.Equal(json, JsonSerializer.Serialize(
            JsonSerializer.Deserialize<PublishedRecordEntry>(json, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<PublishedRecordEntry>(
            json.Replace("\"statement\"", "1", StringComparison.Ordinal), PublicationProtocol.JsonOptions));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("officials")]
    [InlineData("issueIds")]
    [InlineData("policyAreaIds")]
    public void AnEntryWithoutAFilterFieldIsRefusedSoNoReaderGuessesADefault(string field)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(Entry(PublishedRecordKind.Statement, StatementId), PublicationProtocol.JsonOptions))!.AsObject();
        json.Remove(field);

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<PublishedRecordEntry>(json.ToJsonString(), PublicationProtocol.JsonOptions));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void RejectsAnyOtherSchemaVersion(int offset)
    {
        Assert.Throws<InvalidDataException>(Release(PublicationProtocol.SchemaVersion + offset, Entry(PublishedRecordKind.DocumentChange, ChangeId)).Validate);
    }

    [Fact]
    public void AcceptsTheCurrentSchemaVersion()
    {
        Release(PublicationProtocol.SchemaVersion, Entry(PublishedRecordKind.DocumentChange, ChangeId)).Validate();
    }

    [Fact]
    public void FiltersRoundTripSoSearchReadsWhatPublishWrote()
    {
        var entry = Entry(PublishedRecordKind.Statement, StatementId) with
        {
            Officials = [new() { Id = "mayor", Name = "Mayor Example" }],
            IssueIds = ["housing"],
            PolicyAreaIds = ["land-use"],
            SourceHost = "example.test",
            EventDate = new DateOnly(2026, 1, 2)
        };
        var json = JsonSerializer.Serialize(Release(PublicationProtocol.SchemaVersion, entry), PublicationProtocol.JsonOptions);

        var read = JsonSerializer.Deserialize<PublicationRelease>(json, PublicationProtocol.JsonOptions)!;
        read.Validate();

        var filters = Assert.Single(read.Records);
        Assert.Equal(("mayor", "Mayor Example"), (Assert.Single(filters.Officials).Id, filters.Officials[0].Name));
        Assert.Equal(["housing"], filters.IssueIds);
        Assert.Equal(["land-use"], filters.PolicyAreaIds);
        Assert.Equal("example.test", filters.SourceHost);
        Assert.Equal(new DateOnly(2026, 1, 2), filters.EventDate);
        Assert.Contains("\"eventDate\":\"2026-01-02\"", json, StringComparison.Ordinal);
    }

    public static TheoryData<PublishedRecordEntry> InvalidFilters()
    {
        var entry = Entry(PublishedRecordKind.Statement, StatementId);
        var tooLong = new string('x', PublishedRecordEntry.MaximumFilterValueLength + 1);
        return new()
        {
            entry with { IssueIds = [.. Enumerable.Range(0, PublishedRecordEntry.MaximumFilterValues + 1).Select(index => $"issue-{index}")] },
            entry with { Officials = [.. Enumerable.Range(0, PublishedRecordEntry.MaximumFilterValues + 1)
                .Select(index => new PublishedOfficial { Id = $"person-{index}", Name = "Name" })] },
            entry with { PolicyAreaIds = [tooLong] },
            entry with { Officials = [new() { Id = "mayor", Name = tooLong }] },
            entry with { SourceHost = tooLong },
            entry with { SourceHost = " " },
            entry with { Officials = [new() { Id = "mayor", Name = "A" }, new() { Id = "mayor", Name = "B" }] },
            entry with { IssueIds = ["housing", "housing"] },
            entry with { PolicyAreaIds = [" "] },
            entry with { Officials = null! }
        };
    }

    [Theory]
    [MemberData(nameof(InvalidFilters))]
    public void FiltersAreBoundedAndDistinctSoOneEntryCannotSwellTheManifest(PublishedRecordEntry entry)
    {
        var error = Assert.Throws<InvalidDataException>(Release(PublicationProtocol.SchemaVersion, entry).Validate);

        Assert.StartsWith("Release manifest filters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FiltersAtTheirBoundsAreAccepted()
    {
        var atLimit = new string('x', PublishedRecordEntry.MaximumFilterValueLength);
        Release(PublicationProtocol.SchemaVersion, Entry(PublishedRecordKind.Statement, StatementId) with
        {
            IssueIds = [.. Enumerable.Range(0, PublishedRecordEntry.MaximumFilterValues).Select(index => $"issue-{index}")],
            PolicyAreaIds = [atLimit],
            SourceHost = atLimit
        }).Validate();
    }

    [Fact]
    public void AnEntryFromBeforeTheVoteAndCosponsorFieldsStillReadsAndKeepsThemEmptySoNoReaderGuessesAValue()
    {
        var oldJson = JsonSerializer.Serialize(Entry(PublishedRecordKind.Vote, StatementId), PublicationProtocol.JsonOptions);
        var node = JsonNode.Parse(oldJson)!.AsObject();
        foreach (var field in new[] { "yeaCount", "nayCount", "alignment", "cosponsorIds" }) node.Remove(field);

        var read = JsonSerializer.Deserialize<PublishedRecordEntry>(node.ToJsonString(), PublicationProtocol.JsonOptions)!;
        read.Validate();

        Assert.Null(read.YeaCount);
        Assert.Null(read.NayCount);
        Assert.Null(read.Alignment);
        Assert.Null(read.CosponsorIds);
    }

    [Fact]
    public void VoteFactsRoundTripAndTheAlignmentIsAWordSoNoReaderSeesANumber()
    {
        var entry = Entry(PublishedRecordKind.Vote, StatementId) with { YeaCount = 216, NayCount = 214, Alignment = PublishedVoteAlignment.Split };
        var json = JsonSerializer.Serialize(entry, PublicationProtocol.JsonOptions);

        var read = JsonSerializer.Deserialize<PublishedRecordEntry>(json, PublicationProtocol.JsonOptions)!;
        read.Validate();

        Assert.Contains("\"alignment\":\"split\"", json, StringComparison.Ordinal);
        Assert.Equal((216, 214, PublishedVoteAlignment.Split), (read.YeaCount, read.NayCount, read.Alignment));
    }

    public static TheoryData<PublishedRecordEntry> InvalidVoteFacts()
    {
        var vote = Entry(PublishedRecordKind.Vote, StatementId);
        return new()
        {
            vote with { YeaCount = 1 },
            vote with { NayCount = 1 },
            vote with { Alignment = PublishedVoteAlignment.Mixed },
            vote with { YeaCount = -1, NayCount = 0 },
            vote with { YeaCount = PublishedVote.MaximumMembers, NayCount = 1 },
            vote with { YeaCount = 1, NayCount = 1, Alignment = (PublishedVoteAlignment)99 },
            Entry(PublishedRecordKind.Statement, StatementId) with { YeaCount = 1, NayCount = 1 },
            Entry(PublishedRecordKind.Action, new string('7', 64)) with { Alignment = PublishedVoteAlignment.Mixed }
        };
    }

    [Theory]
    [MemberData(nameof(InvalidVoteFacts))]
    public void VoteFactsBelongToAVoteEntryAsAPairWithBoundedCountsSoNoOtherKindCarriesThem(PublishedRecordEntry entry)
    {
        Assert.Throws<InvalidDataException>(entry.Validate);
    }

    [Fact]
    public void AVoteEntryWithCountsAndNoAlignmentIsValidBecauseAProceduralVoteHasNoPartyReading()
    {
        (Entry(PublishedRecordKind.Vote, StatementId) with { YeaCount = 0, NayCount = 0 }).Validate();
    }

    public static TheoryData<PublishedRecordEntry> InvalidCosponsors()
    {
        var action = Entry(PublishedRecordKind.Action, new string('7', 64));
        var tooLong = new string('x', PublishedRecordEntry.MaximumFilterValueLength + 1);
        return new()
        {
            Entry(PublishedRecordKind.Statement, StatementId) with { CosponsorIds = [] },
            action with { CosponsorIds = [.. Enumerable.Range(0, PublishedAction.MaximumSponsorships + 1).Select(index => $"person-{index}")] },
            action with { CosponsorIds = ["chair", "chair"] },
            action with { CosponsorIds = [tooLong] },
            action with { CosponsorIds = [" "] },
            action with { CosponsorIds = [null!] }
        };
    }

    [Theory]
    [MemberData(nameof(InvalidCosponsors))]
    public void CosponsorsBelongToAnActionEntryAndAreDistinctAndBoundedSoOneBillCannotSwellTheManifest(PublishedRecordEntry entry)
    {
        Assert.Throws<InvalidDataException>(entry.Validate);
    }

    [Fact]
    public void AnActionEntryMayHoldAsManyCosponsorsAsTheRecordHoldsSponsorships()
    {
        (Entry(PublishedRecordKind.Action, new string('7', 64)) with
        {
            CosponsorIds = [.. Enumerable.Range(0, PublishedAction.MaximumSponsorships).Select(index => $"person-{index}")]
        }).Validate();
    }

    [Fact]
    public void AManifestOfTheMaximumNumberOfRealisticActionEntriesWithCosponsorsStaysUnderTheManifestByteCap()
    {
        var entries = Enumerable.Range(0, PublicationProtocol.MaximumRecordsPerRelease).Select(index => Entry(PublishedRecordKind.Action, index.ToString("x").PadLeft(64, '0')) with
        {
            Headline = "Representative Example introduced a bill to fund local housing and transit projects in the district",
            Officials = [new() { Id = "representative-example", Name = "Representative Example" }],
            IssueIds = ["housing-affordability", "public-transit", "local-budgets"],
            PolicyAreaIds = ["housing-and-community-development"],
            SourceHost = "api.congress.gov",
            BillTitle = "A bill to provide grants to States and local governments for housing and transit projects, and for other purposes",
            EventDate = new DateOnly(2026, 1, 20),
            CosponsorIds = [.. Enumerable.Range(0, 15).Select(cosponsor => $"cosponsor-{cosponsor:D2}-of-a-long-name")]
        }).ToArray();
        var release = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = 1,
            PublishedAtUtc = DateTimeOffset.UnixEpoch,
            Records = entries,
            Withdrawals = []
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(release, PublicationProtocol.JsonOptions).Length;

        release.Validate();
        Assert.True(bytes < PublicationProtocol.MaximumManifestBytes, $"The manifest holds {bytes} bytes.");
    }

    [Theory]
    [InlineData(PublishedRecordKind.Statement, 32)]
    [InlineData(PublishedRecordKind.DocumentChange, 64)]
    public void EntryIdMustHaveTheShapeOfItsKind(PublishedRecordKind kind, int length)
    {
        Assert.Throws<InvalidDataException>(Entry(kind, new string('a', length)).Validate);
    }

    [Fact]
    public void ARecordIdIsUniqueAcrossKindsBecauseAllKindsShareOneRecordFilePath()
    {
        Release(PublicationProtocol.SchemaVersion, Entry(PublishedRecordKind.Statement, StatementId), Entry(PublishedRecordKind.Action, new string('7', 64))).Validate();
        Assert.Throws<InvalidDataException>(Release(PublicationProtocol.SchemaVersion, Entry(PublishedRecordKind.Statement, StatementId),
            Entry(PublishedRecordKind.Action, StatementId)).Validate);
        Assert.Throws<InvalidDataException>(Release(PublicationProtocol.SchemaVersion, Entry(PublishedRecordKind.DocumentChange, ChangeId),
            Entry(PublishedRecordKind.DocumentChange, ChangeId)).Validate);
    }

    [Fact]
    public void ARecordIdIsUniqueAcrossRecordsAndWithdrawalsBecauseOneNoticePageUsesTheRecordPagePath()
    {
        var withdrawal = Withdrawal(PublishedRecordKind.Action, new string('7', 64));
        (Release(PublicationProtocol.SchemaVersion, Entry(PublishedRecordKind.Statement, StatementId)) with { Withdrawals = [withdrawal] }).Validate();
        Assert.Throws<InvalidDataException>((Release(PublicationProtocol.SchemaVersion, Entry(PublishedRecordKind.Statement, StatementId))
            with
        { Withdrawals = [withdrawal with { Kind = PublishedRecordKind.Statement, RecordId = StatementId }] }).Validate);
        Assert.Throws<InvalidDataException>((Release(PublicationProtocol.SchemaVersion) with { Withdrawals = [withdrawal, withdrawal] }).Validate);
    }

    [Fact]
    public void RecordsAndWithdrawalsShareOneCap()
    {
        PublishedRecordEntry[] entries = [.. Enumerable.Range(0, PublicationProtocol.MaximumRecordsPerRelease - 1)
            .Select(index => Entry(PublishedRecordKind.Statement, index.ToString("x64")))];
        PublishedWithdrawal[] one = [Withdrawal(PublishedRecordKind.Action, new string('f', 64))];

        (Release(PublicationProtocol.SchemaVersion, entries) with { Withdrawals = one }).Validate();
        Assert.Throws<InvalidDataException>((Release(PublicationProtocol.SchemaVersion, entries)
            with
        { Withdrawals = [.. one, Withdrawal(PublishedRecordKind.Action, new string('e', 64))] }).Validate);
    }

    [Fact]
    public void AManifestWithAnInvalidOrMissingWithdrawalListIsRefused()
    {
        var release = Release(PublicationProtocol.SchemaVersion, Entry(PublishedRecordKind.Statement, StatementId));

        Assert.Throws<InvalidDataException>((release with { Withdrawals = null! }).Validate);
        Assert.Throws<InvalidDataException>((release with { Withdrawals = [null!] }).Validate);
        Assert.Throws<InvalidDataException>((release with { Withdrawals = [Withdrawal(PublishedRecordKind.Action, "short")] }).Validate);
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<PublicationRelease>(
            """{"schemaVersion":1,"releaseNumber":1,"publishedAtUtc":"1970-01-01T00:00:00+00:00","records":[]}""", PublicationProtocol.JsonOptions));
    }

    private static PublishedWithdrawal Withdrawal(PublishedRecordKind kind, string id) => new()
    {
        Kind = kind,
        RecordId = id,
        WithdrawnRevisionNumber = 1,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
        WithdrawnAtUtc = DateTimeOffset.UnixEpoch.AddDays(1),
        Note = "Wrong.",
        Corrections = []
    };

    private static PublicationRelease Release(int version, params PublishedRecordEntry[] entries) => new()
    {
        SchemaVersion = version,
        ReleaseNumber = 1,
        PublishedAtUtc = DateTimeOffset.UnixEpoch,
        Withdrawals = [],
        Records = entries
    };

    private static PublishedRecordEntry Entry(PublishedRecordKind kind, string id) => new()
    {
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = [],
        Kind = kind,
        RecordId = id,
        RevisionNumber = 1,
        Headline = "Headline",
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
        BillTitle = kind == PublishedRecordKind.Action ? "A bill" : null
    };
}
