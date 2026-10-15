using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublicationReleaseTests
{
    private static readonly string ChangeId = new('a', 32);
    private static readonly string StatementId = new('b', 64);

    [Fact]
    public void VersionOneManifestWithoutKindsStillLoadsAndMeansDocumentChanges()
    {
        var json = $$"""{"schemaVersion":1,"releaseNumber":3,"publishedAtUtc":"2026-01-01T00:00:00+00:00","records":[{"recordId":"{{ChangeId}}","revisionNumber":1,"headline":"Old","firstPublishedAtUtc":"2026-01-01T00:00:00+00:00"}]}""";

        var release = JsonSerializer.Deserialize<PublicationRelease>(json, PublicationProtocol.JsonOptions)!;
        release.Validate();

        Assert.Equal(PublishedRecordKind.DocumentChange, Assert.Single(release.Records).Kind);
    }

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
    [InlineData(0)]
    [InlineData(4)]
    public void RejectsASchemaVersionThisReaderDoesNotKnow(int version)
    {
        Assert.Throws<InvalidDataException>(Release(version, Entry(PublishedRecordKind.DocumentChange, ChangeId)).Validate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AcceptsEverySchemaVersionStillOnDisk(int version)
    {
        Release(version, Entry(PublishedRecordKind.DocumentChange, ChangeId)).Validate();
    }

    [Fact]
    public void SchemaThreeFiltersRoundTripSoSearchReadsWhatPublishWrote()
    {
        var entry = Entry(PublishedRecordKind.Statement, StatementId) with
        {
            Officials = [new() { Id = "mayor", Name = "Mayor Example" }],
            IssueIds = ["housing"],
            PolicyAreaIds = ["land-use"],
            SourceHost = "example.test",
            EventDate = new DateOnly(2026, 1, 2)
        };
        var json = JsonSerializer.Serialize(Release(3, entry), PublicationProtocol.JsonOptions);

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

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "\"kind\":\"statement\",")]
    public void OlderManifestsReadWithEmptyFiltersBecauseNothingIsBackfilled(int version, string kind)
    {
        var id = version == 1 ? ChangeId : StatementId;
        var json = $$"""{"schemaVersion":{{version}},"releaseNumber":3,"publishedAtUtc":"2026-01-01T00:00:00+00:00","records":[{{{kind}}"recordId":"{{id}}","revisionNumber":1,"headline":"Old","firstPublishedAtUtc":"2026-01-01T00:00:00+00:00"}]}""";

        var release = JsonSerializer.Deserialize<PublicationRelease>(json, PublicationProtocol.JsonOptions)!;
        release.Validate();

        var entry = Assert.Single(release.Records);
        Assert.Empty(entry.Officials);
        Assert.Empty(entry.IssueIds);
        Assert.Empty(entry.PolicyAreaIds);
        Assert.Null(entry.SourceHost);
        Assert.Null(entry.EventDate);
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
        var error = Assert.Throws<InvalidDataException>(Release(3, entry).Validate);

        Assert.StartsWith("Release manifest filters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FiltersAtTheirBoundsAreAccepted()
    {
        var atLimit = new string('x', PublishedRecordEntry.MaximumFilterValueLength);
        Release(3, Entry(PublishedRecordKind.Statement, StatementId) with
        {
            IssueIds = [.. Enumerable.Range(0, PublishedRecordEntry.MaximumFilterValues).Select(index => $"issue-{index}")],
            PolicyAreaIds = [atLimit],
            SourceHost = atLimit
        }).Validate();
    }

    [Theory]
    [InlineData(PublishedRecordKind.Statement, 32)]
    [InlineData(PublishedRecordKind.DocumentChange, 64)]
    public void EntryIdMustHaveTheShapeOfItsKind(PublishedRecordKind kind, int length)
    {
        Assert.Throws<InvalidDataException>(Entry(kind, new string('a', length)).Validate);
    }

    [Fact]
    public void SameIdUnderTwoKindsIsTwoRecordsButTheSamePairTwiceIsNot()
    {
        Release(2, Entry(PublishedRecordKind.DocumentChange, ChangeId), Entry(PublishedRecordKind.Statement, StatementId)).Validate();
        Assert.Throws<InvalidDataException>(Release(2, Entry(PublishedRecordKind.DocumentChange, ChangeId),
            Entry(PublishedRecordKind.DocumentChange, ChangeId)).Validate);
    }

    private static PublicationRelease Release(int version, params PublishedRecordEntry[] entries) => new()
    {
        SchemaVersion = version,
        ReleaseNumber = 1,
        PublishedAtUtc = DateTimeOffset.UnixEpoch,
        Records = entries
    };

    private static PublishedRecordEntry Entry(PublishedRecordKind kind, string id) => new()
    {
        Kind = kind,
        RecordId = id,
        RevisionNumber = 1,
        Headline = "Headline",
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
    };
}
