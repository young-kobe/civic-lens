using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublicationProtocolTests
{
    [Theory]
    [InlineData(32, true)]
    [InlineData(64, true)]
    [InlineData(31, false)]
    [InlineData(33, false)]
    [InlineData(65, false)]
    public void AnyRecordIdHasExactlyThe32Or64HexLengthsSoARouteCanRefuseAnIdBeforeTheLookup(int length, bool expected)
    {
        Assert.Equal(expected, PublicationProtocol.IsAnyRecordId(new string('a', length)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEmptyRecordIdIsNotValid(string? value)
    {
        Assert.False(PublicationProtocol.IsAnyRecordId(value));
    }

    [Fact]
    public void UpperCaseAndNonHexCharactersAreNotValidInARecordId()
    {
        Assert.False(PublicationProtocol.IsAnyRecordId(new string('A', 64)));
        Assert.False(PublicationProtocol.IsAnyRecordId(new string('g', 32)));
    }

    private static string TitleOf(string link) =>
        Uri.UnescapeDataString(link[(link.IndexOf("&title=", StringComparison.Ordinal) + "&title=".Length)..]);

    [Fact]
    public void TheReportLinkCarriesTheControlledKindAndEncodedTitleAfterTheRecordAndRevision()
    {
        var link = PublicationProtocol.ReportPageLink(new string('a', 32), 3, PublishedRecordKind.Vote, "Senate votes: 51 & 49 <b>");

        Assert.Equal($"report.html?recordId={new string('a', 32)}&revision=3&kind=vote&title=Senate%20votes%3A%2051%20%26%2049%20%3Cb%3E", link);
    }

    [Theory]
    [InlineData(PublishedRecordKind.DocumentChange, "documentChange")]
    [InlineData(PublishedRecordKind.Statement, "statement")]
    [InlineData(PublishedRecordKind.Action, "action")]
    [InlineData(PublishedRecordKind.Brief, "brief")]
    [InlineData(PublishedRecordKind.Vote, "vote")]
    [InlineData(PublishedRecordKind.Relationship, "relationship")]
    [InlineData(PublishedRecordKind.ExecutiveAction, "executiveAction")]
    public void TheReportLinkUsesOnlyTheKnownWireKind(PublishedRecordKind kind, string wireKind)
    {
        Assert.Equal($"report.html?recordId=a&revision=1&kind={wireKind}&title=Title",
            PublicationProtocol.ReportPageLink("a", 1, kind, "Title"));
    }

    [Fact]
    public void TheReportLinkRejectsAnUnknownKind()
    {
        Assert.Throws<JsonException>(() => PublicationProtocol.ReportPageLink("a", 1, (PublishedRecordKind)99, "Title"));
    }

    [Fact]
    public void TheReportLinkCutsALongTitleOnAWordBoundaryWithinTheLimitSoTheLinkStaysShort()
    {
        var words = string.Join(' ', Enumerable.Repeat("housing", 14));
        var title = $"{words} {new string('x', 30)}";

        var shown = TitleOf(PublicationProtocol.ReportPageLink("a", 1, PublishedRecordKind.Action, title));

        Assert.Equal($"{words}\u2026", shown);
    }

    [Fact]
    public void TheReportLinkKeepsATitleAtTheLimitAndCutsOneCharacterOver()
    {
        var limit = ReaderReportProtocol.MaximumTitleCharacters;

        Assert.Equal(new string('x', limit), TitleOf(PublicationProtocol.ReportPageLink("a", 1, PublishedRecordKind.Vote, new string('x', limit))));
        Assert.Equal(limit, TitleOf(PublicationProtocol.ReportPageLink("a", 1, PublishedRecordKind.Vote, new string('x', limit + 1))).Length);
    }

    [Fact]
    public void TheReportLinkNeverSplitsASurrogatePairAndOmitsABlankTitle()
    {
        var cut = TitleOf(PublicationProtocol.ReportPageLink("a", 1, PublishedRecordKind.Vote, string.Concat(Enumerable.Repeat("\U0001F600", 100))));

        Assert.True(cut.Length <= ReaderReportProtocol.MaximumTitleCharacters);
        Assert.False(char.IsHighSurrogate(cut[^2]));
        Assert.DoesNotContain("title", PublicationProtocol.ReportPageLink("a", 1, PublishedRecordKind.Vote, "  "), StringComparison.Ordinal);
    }
}
