using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishedRecordReferenceTests
{
    [Theory]
    [InlineData(PublishedRecordKind.DocumentChange, "documentChange:abc")]
    [InlineData(PublishedRecordKind.Statement, "statement:abc")]
    [InlineData(PublishedRecordKind.Action, "action:abc")]
    public void FormattingAndParsingRoundTripSoTheFormAndTheCommandAgreeOnOneShape(PublishedRecordKind kind, string text)
    {
        var reference = new PublishedRecordReference(kind, "abc");

        Assert.Equal(text, reference.ToString());
        Assert.Equal(reference, PublishedRecordReference.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(":abc")]
    [InlineData("statement:")]
    [InlineData("Statement:abc")]
    [InlineData("draft:abc")]
    [InlineData("1:abc")]
    public void AMissingOrUnknownKindIsRejectedBecauseTheKindIsNeverGuessedFromTheId(string? text)
    {
        Assert.Throws<ArgumentException>(() => PublishedRecordReference.Parse(text));
    }

    [Fact]
    public void TheIdKeepsAnyFurtherSeparatorSoTheIdValidatorOwnsItsShape()
    {
        Assert.Equal("a:b", PublishedRecordReference.Parse("statement:a:b").RecordId);
    }

    [Fact]
    public void TheKindSpellingMatchesTheWireSpellingOfRecordsJson()
    {
        foreach (var kind in Enum.GetValues<PublishedRecordKind>())
            Assert.Equal(JsonSerializer.Serialize(kind), $"\"{PublishedRecordReference.NameOf(kind)}\"");
    }
}
