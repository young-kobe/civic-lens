using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedBillStatusesTests
{
    private static readonly DateTimeOffset Observed = new(2026, 1, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AStatusFileKeepsItsRowsThroughAJsonRoundTrip()
    {
        var file = new PublishedBillStatuses { Records = [Row('a'), Row('b') with { Text = "Became Public Law No: 119-21." }] };

        var read = JsonSerializer.Deserialize<PublishedBillStatuses>(
            JsonSerializer.Serialize(file, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions)!;
        read.Validate();

        Assert.Equal(new DateOnly(2026, 1, 28), read.Records[0].Date);
        Assert.Equal(Observed, read.Records[1].ObservedAtUtc);
        Assert.Equal("Became Public Law No: 119-21.", read.Records[1].Text);
    }

    public static TheoryData<PublishedBillStatus> InvalidRows() => new()
    {
        Row('a') with { RecordId = "not-a-record-id" },
        Row('a') with { Text = " " },
        Row('a') with { Text = " padded " },
        Row('a') with { Text = new string('x', PublishedBillStatus.MaximumTextLength + 1) },
        Row('a') with { Text = "Passed\u0000 House." },
        Row('a') with { Text = "Passed\tHouse." },
        Row('a') with { ObservedAtUtc = new DateTimeOffset(2026, 1, 30, 9, 0, 0, TimeSpan.FromHours(2)) }
    };

    [Theory]
    [MemberData(nameof(InvalidRows))]
    public void ARowThatBreaksTheRulesIsRefusedSoNoReaderShowsAHalfTrueStatus(PublishedBillStatus row)
    {
        Assert.Throws<InvalidDataException>(() => new PublishedBillStatuses { Records = [row] }.Validate());
    }

    [Fact]
    public void ADuplicateRecordIsRefusedBecauseOneActionHasOneStatus()
    {
        Assert.Throws<InvalidDataException>(() => new PublishedBillStatuses { Records = [Row('a'), Row('a')] }.Validate());
    }

    [Fact]
    public void ARowWithNoDateIsRefusedOnReadSoNoReaderGuessesADefault()
    {
        var json = $$"""{"records":[{"recordId":"{{new string('a', 64)}}","text":"Passed House.","observedAtUtc":"2026-01-30T09:00:00+00:00"}]}""";

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<PublishedBillStatuses>(json, PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void AFileOfTheMaximumNumberOfRealisticRowsStaysUnderTheByteCapSoTheCapNeverRefusesARealRelease()
    {
        var rows = Enumerable.Range(0, PublicationProtocol.MaximumRecordsPerRelease)
            .Select(index => Row('a') with { RecordId = index.ToString("x64"), Text = new string('x', 700) });
        var file = new PublishedBillStatuses { Records = [.. rows] };

        file.Validate();

        Assert.True(JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions).Length < PublishedBillStatuses.MaximumBytes);
    }

    [Fact]
    public void ARowOfTheLongestAllowedTextStaysUnderTheByteCapOfARowEvenWithTheWidestCharacters()
    {
        var widest = new PublishedBillStatus { RecordId = new string('a', 64), Text = new string('\u20ac', PublishedBillStatus.MaximumTextLength), Date = new DateOnly(2026, 1, 28), ObservedAtUtc = Observed };

        widest.Validate();

        Assert.True(JsonSerializer.SerializeToUtf8Bytes(widest, PublicationProtocol.JsonOptions).Length <= PublishedBillStatuses.MaximumBytesPerRow);
    }

    private static PublishedBillStatus Row(char id) =>
        new() { RecordId = new string(id, 64), Text = "Passed House.", Date = new DateOnly(2026, 1, 28), ObservedAtUtc = Observed };
}
