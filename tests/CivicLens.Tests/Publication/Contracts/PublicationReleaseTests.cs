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

        Assert.Equal(entry, JsonSerializer.Deserialize<PublishedRecordEntry>(json, PublicationProtocol.JsonOptions));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<PublishedRecordEntry>(
            json.Replace("\"statement\"", "1", StringComparison.Ordinal), PublicationProtocol.JsonOptions));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void RejectsASchemaVersionThisReaderDoesNotKnow(int version)
    {
        Assert.Throws<InvalidDataException>(Release(version, Entry(PublishedRecordKind.DocumentChange, ChangeId)).Validate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AcceptsEverySchemaVersionStillOnDisk(int version)
    {
        Release(version, Entry(PublishedRecordKind.DocumentChange, ChangeId)).Validate();
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
