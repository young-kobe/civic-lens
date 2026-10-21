using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedWithdrawalTests
{
    private static readonly PublishedWithdrawal Valid = new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = new string('a', 64),
        WithdrawnRevisionNumber = 3,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
        WithdrawnAtUtc = DateTimeOffset.UnixEpoch.AddDays(2),
        Note = "The roll call was entered wrong.",
        Corrections = [new() { ReplacedRevisionNumber = 1, PublishedAtUtc = DateTimeOffset.UnixEpoch.AddDays(1), Note = "Fixed a date." }]
    };

    [Fact]
    public void AcceptsAWithdrawalWithItsHistoryAndRoundTripsThroughJson()
    {
        Valid.Validate();

        var json = JsonSerializer.Serialize(Valid, PublicationProtocol.JsonOptions);

        Assert.Equal(Valid.Note, JsonSerializer.Deserialize<PublishedWithdrawal>(json, PublicationProtocol.JsonOptions)!.Note);
        Assert.Contains("\"kind\":\"vote\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsARecordIdThatDoesNotFitItsKind()
    {
        Assert.Throws<InvalidDataException>((Valid with { RecordId = new string('a', 32) }).Validate);
        Assert.Throws<InvalidDataException>((Valid with { Kind = PublishedRecordKind.DocumentChange }).Validate);
    }

    [Fact]
    public void RejectsARevisionBelowOneAndAWithdrawalDateBeforeThePublicationDate()
    {
        Assert.Throws<InvalidDataException>((Valid with { WithdrawnRevisionNumber = 0 }).Validate);
        Assert.Throws<InvalidDataException>((Valid with { WithdrawnAtUtc = Valid.FirstPublishedAtUtc.AddSeconds(-1) }).Validate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0note")]
    public void RejectsABlankOrNulNote(string? note)
    {
        Assert.Throws<InvalidDataException>((Valid with { Note = note! }).Validate);
    }

    [Fact]
    public void RejectsANoteOverTheLimitAndAcceptsOneAtTheLimit()
    {
        (Valid with { Note = new string('a', PublishedCorrection.MaximumNoteLength) }).Validate();
        Assert.Throws<InvalidDataException>((Valid with { Note = new string('a', PublishedCorrection.MaximumNoteLength + 1) }).Validate);
    }

    [Fact]
    public void RejectsCorrectionsThatReplaceTheWithdrawnRevisionOrALaterOne()
    {
        var late = new PublishedCorrection { ReplacedRevisionNumber = 3, PublishedAtUtc = DateTimeOffset.UnixEpoch, Note = "x" };

        Assert.Throws<InvalidDataException>((Valid with { Corrections = [late] }).Validate);
        Assert.Throws<InvalidDataException>((Valid with { Corrections = null! }).Validate);
    }
}
