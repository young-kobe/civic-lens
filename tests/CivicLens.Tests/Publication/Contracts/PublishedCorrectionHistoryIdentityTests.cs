using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedCorrectionHistoryIdentityTests
{
    [Fact]
    public void MatchesTheVersionOneBinaryProtocolVector()
    {
        var entries = new PublishedCorrection[]
        {
            Entry(9, new DateTimeOffset(2026, 5, 6, 7, 8, 9, 123, TimeSpan.FromMinutes(90)).AddTicks(4560), "Café\u001fe\u0301\nline"),
            Entry(3, new DateTimeOffset(2026, 8, 2, 3, 4, 5, 678, TimeSpan.FromMinutes(-240)).AddTicks(9010), factsUnchanged: true)
        };

        PublishedCorrection.ValidateAll(entries, 10, PublishedRecordKind.Vote);

        Assert.Equal("471b703c6e65089c328d2639d44f8b9bdbedb4a74adc29e6ccbac365efddefeb", PublishedCorrectionHistoryIdentity.Of(entries));
    }

    [Fact]
    public void SupportsAValidatedHistoryWithTwentyUnicodeNotes()
    {
        var entries = Enumerable.Range(1, PublishedCorrection.MaximumNotedEntries)
            .Select(number => Entry(21 - number, DateTimeOffset.UnixEpoch, $"Fix | line\n{number}: Café e\u0301 \U00010400"))
            .ToArray();

        PublishedCorrection.ValidateAll(entries, 21, PublishedRecordKind.Vote);

        var original = PublishedCorrectionHistoryIdentity.Of(entries);
        entries[^1] = entries[^1] with { Note = "Another correction" };
        Assert.NotEqual(original, PublishedCorrectionHistoryIdentity.Of(entries));
    }

    [Fact]
    public void EachContentFieldChangesTheIdentity()
    {
        var timestamp = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var baseline = Entry(2, timestamp, "Note");
        var id = PublishedCorrectionHistoryIdentity.Of([baseline]);

        Assert.NotEqual(id, PublishedCorrectionHistoryIdentity.Of([baseline with { ReplacedRevisionNumber = 3 }]));
        Assert.NotEqual(id, PublishedCorrectionHistoryIdentity.Of([baseline with { PublishedAtUtc = timestamp.AddTicks(1) }]));
        Assert.NotEqual(id, PublishedCorrectionHistoryIdentity.Of([baseline with { Note = "note" }]));
        Assert.NotEqual(id, PublishedCorrectionHistoryIdentity.Of([baseline with { Note = null }]));
        var other = Entry(1, timestamp, factsUnchanged: true);
        Assert.NotEqual(id, PublishedCorrectionHistoryIdentity.Of([baseline, other]));
        Assert.NotEqual(PublishedCorrectionHistoryIdentity.Of([other]),
            PublishedCorrectionHistoryIdentity.Of([other with { FactsUnchanged = false }]));
    }

    [Fact]
    public void PreservesOffsetWhitespaceAndUnicodeNormalization()
    {
        var instant = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var utc = Entry(1, instant, " Café ");
        var sameInstantWithOffset = Entry(1, instant.ToOffset(TimeSpan.FromHours(2)), " Café ");
        var trimmed = Entry(1, instant, "Café");
        var decomposed = Entry(1, instant, " Cafe\u0301 ");

        Assert.Equal(instant, sameInstantWithOffset.PublishedAtUtc);
        Assert.NotEqual(PublishedCorrectionHistoryIdentity.Of([utc]), PublishedCorrectionHistoryIdentity.Of([sameInstantWithOffset]));
        Assert.NotEqual(PublishedCorrectionHistoryIdentity.Of([utc]), PublishedCorrectionHistoryIdentity.Of([trimmed]));
        Assert.NotEqual(PublishedCorrectionHistoryIdentity.Of([utc]), PublishedCorrectionHistoryIdentity.Of([decomposed]));
    }

    [Fact]
    public void RejectsNullInputEntriesAndUnsupportedVersions()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrectionHistoryIdentity.Of(null!));
        Assert.Throws<InvalidDataException>(() => PublishedCorrectionHistoryIdentity.Of([null!]));
        Assert.Throws<InvalidDataException>(() => PublishedCorrectionHistoryIdentity.Of([], 2));
    }

    [Fact]
    public void RejectsAnInvalidUtf16NoteInsteadOfReplacingItsCodeUnit()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrectionHistoryIdentity.Of([Entry(1, DateTimeOffset.UnixEpoch, "bad\uD800note")]));
    }

    private static PublishedCorrection Entry(int replaced, DateTimeOffset publishedAt, string? note = null, bool factsUnchanged = false) =>
        new() { ReplacedRevisionNumber = replaced, PublishedAtUtc = publishedAt, Note = note, FactsUnchanged = factsUnchanged };
}
