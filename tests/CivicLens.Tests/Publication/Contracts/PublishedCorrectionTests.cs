using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedCorrectionTests
{
    private const PublishedTextRewrite Model = PublishedTextRewrite.Model;
    private const PublishedTextRewrite Code = PublishedTextRewrite.Code;

    private static PublishedCorrection Entry(int replaced, string? note = null, PublishedTextRewrite rewrite = PublishedTextRewrite.None) =>
        new() { ReplacedRevisionNumber = replaced, PublishedAtUtc = DateTimeOffset.UnixEpoch, Note = note, TextRewrite = rewrite };

    [Fact]
    public void ATextRewriteAndASourceUpdateKeepTheirOwnEntriesNextToEachOther()
    {
        PublishedCorrection.ValidateAll([Entry(2, rewrite: Model), Entry(1)], 3, PublishedRecordKind.Vote);
        PublishedCorrection.ValidateAll([Entry(2), Entry(1, rewrite: Code)], 3, PublishedRecordKind.Vote);
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(2, rewrite: Model), Entry(1, rewrite: Model)], 3, PublishedRecordKind.Vote));
    }

    [Fact]
    public void AModelRewriteAndACodeRewriteAreOneKindSoARunHoldsOnlyOneOfThemAndTheEntryCapStays()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(2, rewrite: Code), Entry(1, rewrite: Model)], 3, PublishedRecordKind.Vote));
        PublishedCorrection.ValidateAll([Entry(3, rewrite: Code), Entry(2, "Fix."), Entry(1, rewrite: Model)], 4, PublishedRecordKind.Vote);
    }

    [Fact]
    public void AnUnknownTextRewriteIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, rewrite: (PublishedTextRewrite)3)], 2, PublishedRecordKind.Vote));
    }

    [Fact]
    public void ARunBetweenPersonCorrectionsHoldsAtMostOneCodeUpdateOfEachKind()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(3), Entry(2, rewrite: Model), Entry(1)], 4, PublishedRecordKind.Vote));
        PublishedCorrection.ValidateAll([Entry(4), Entry(3, rewrite: Model), Entry(2, "Fix."), Entry(1)], 5, PublishedRecordKind.Vote);
    }

    [Fact]
    public void ANotedEntryCannotNameATextRewrite()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, "Fix.", rewrite: Model)], 2, PublishedRecordKind.Vote));
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, "Fix.", rewrite: Code)], 2, PublishedRecordKind.Vote));
    }

    [Fact]
    public void PrependKeepsOneCodeUpdateOfEachKindInTheNewestRun()
    {
        var textOnly = Entry(2, rewrite: Model);

        Assert.Equal([textOnly], PublishedCorrection.Prepend([Entry(1, rewrite: Model)], textOnly));
        Assert.Equal([textOnly, Entry(1)], PublishedCorrection.Prepend([Entry(1)], textOnly));
        Assert.Equal([Entry(3), textOnly], PublishedCorrection.Prepend([textOnly, Entry(1)], Entry(3)));
        Assert.Equal([Entry(3), Entry(2, "Fix.")], PublishedCorrection.Prepend([Entry(2, "Fix.")], Entry(3)));
        Assert.Equal([Entry(3, rewrite: Code), Entry(1)], PublishedCorrection.Prepend([textOnly, Entry(1)], Entry(3, rewrite: Code)));
    }

    [Fact]
    public void AnOlderRecordFileWithoutTheFieldReadsAsASourceUpdateAndAPlainEntryWritesNoField()
    {
        const string older = """{"replacedRevisionNumber":1,"publishedAtUtc":"2026-02-02T09:00:00+00:00"}""";

        var read = JsonSerializer.Deserialize<PublishedCorrection>(older, PublicationProtocol.JsonOptions)!;

        Assert.Equal(PublishedTextRewrite.None, read.TextRewrite);
        Assert.DoesNotContain("textRewrite", JsonSerializer.Serialize(read, PublicationProtocol.JsonOptions));
        Assert.Contains("\"textRewrite\":\"model\"", JsonSerializer.Serialize(Entry(1, rewrite: Model), PublicationProtocol.JsonOptions));
        Assert.Contains("\"textRewrite\":\"code\"", JsonSerializer.Serialize(Entry(1, rewrite: Code), PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void AnEarlierRecordFileWithFactsUnchangedReadsAsAModelRewriteAndAWriteNeverWritesTheOldField()
    {
        const string earlier = """{"replacedRevisionNumber":1,"publishedAtUtc":"2026-02-02T09:00:00+00:00","factsUnchanged":true}""";
        const string plain = """{"replacedRevisionNumber":1,"publishedAtUtc":"2026-02-02T09:00:00+00:00","factsUnchanged":false}""";

        var read = JsonSerializer.Deserialize<PublishedCorrection>(earlier, PublicationProtocol.JsonOptions)!;

        Assert.Equal(Entry(1, rewrite: Model) with { PublishedAtUtc = read.PublishedAtUtc }, read);
        Assert.Equal(PublishedTextRewrite.None, JsonSerializer.Deserialize<PublishedCorrection>(plain, PublicationProtocol.JsonOptions)!.TextRewrite);
        var written = JsonSerializer.Serialize(read, PublicationProtocol.JsonOptions);
        Assert.DoesNotContain("factsUnchanged", written);
        Assert.Contains("\"textRewrite\":\"model\"", written);
    }

    [Fact]
    public void AcceptsEntriesFromTheNewestReplacedRevisionDown()
    {
        PublishedCorrection.ValidateAll([Entry(4, "Fixed a date."), Entry(2), Entry(1, "Typo.")], 5, PublishedRecordKind.Vote);
    }

    [Fact]
    public void RejectsEntriesThatAreNotStrictlyDecreasingSoTheListShowsOneHistory()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(2, "a"), Entry(2, "b")], 3, PublishedRecordKind.Vote));
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, "a"), Entry(2, "b")], 3, PublishedRecordKind.Vote));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    public void RejectsAReplacedRevisionThatIsNotAnEarlierRevisionOfTheRecord(int replaced)
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(replaced)], 3, PublishedRecordKind.Vote));
    }

    [Fact]
    public void RejectsMoreThanTheMaximumNotedEntries()
    {
        var list = Enumerable.Range(1, PublishedCorrection.MaximumNotedEntries + 1).Reverse().Select(number => Entry(number, "Fix.")).ToArray();

        PublishedCorrection.ValidateAll(list[1..], 30, PublishedRecordKind.Vote);
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll(list, 30, PublishedRecordKind.Vote));
    }

    [Fact]
    public void RejectsTwoCodeUpdatesNextToEachOtherBecauseARunOfThemIsOneEntry()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(2), Entry(1)], 3, PublishedRecordKind.Vote));
    }

    [Fact]
    public void AcceptsTwoCodeUpdatesAroundEachNotedEntrySoTheListHoldsAtMostSixtyTwo()
    {
        var list = new List<PublishedCorrection>();
        var number = 62;
        for (var gap = 0; gap <= PublishedCorrection.MaximumNotedEntries; gap++)
        {
            list.Add(Entry(number--, rewrite: Model));
            list.Add(Entry(number--));
            if (gap < PublishedCorrection.MaximumNotedEntries) list.Add(Entry(number--, "Fix."));
        }

        PublishedCorrection.ValidateAll([.. list], 63, PublishedRecordKind.Vote);
        Assert.Equal(62, list.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0note")]
    public void RejectsABlankOrNulNoteBecauseANullNoteAlreadyMeansNoNote(string note)
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, note)], 2, PublishedRecordKind.Vote));
    }

    [Fact]
    public void RejectsANoteOverTheMaximumLength()
    {
        PublishedCorrection.ValidateAll([Entry(1, new string('a', PublishedCorrection.MaximumNoteLength))], 2, PublishedRecordKind.Vote);
        Assert.Throws<InvalidDataException>(() =>
            PublishedCorrection.ValidateAll([Entry(1, new string('a', PublishedCorrection.MaximumNoteLength + 1))], 2, PublishedRecordKind.Vote));
    }

    [Fact]
    public void OnlyAVoteCorrectionCanNameAModelRewriteAndOnlyAVoteOrABillActionCorrectionACodeRewrite()
    {
        PublishedCorrection.ValidateAll([Entry(1, rewrite: Model)], 2, PublishedRecordKind.Vote);
        PublishedCorrection.ValidateAll([Entry(1, rewrite: Code)], 2, PublishedRecordKind.Vote);
        PublishedCorrection.ValidateAll([Entry(1, rewrite: Code)], 2, PublishedRecordKind.Action);
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, rewrite: Model)], 2, PublishedRecordKind.Action));
        foreach (var kind in Enum.GetValues<PublishedRecordKind>().Where(kind => kind is not (PublishedRecordKind.Vote or PublishedRecordKind.Action)))
        {
            PublishedCorrection.ValidateAll([Entry(1)], 2, kind);
            Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, rewrite: Model)], 2, kind));
            Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, rewrite: Code)], 2, kind));
        }
    }

    [Theory]
    [InlineData(PublishedTextOrigin.Code, PublishedTextRewrite.Code)]
    [InlineData(PublishedTextOrigin.Model, PublishedTextRewrite.Model)]
    public void TheOriginOfTheNewTextNamesWhoRewroteIt(PublishedTextOrigin origin, PublishedTextRewrite expected)
    {
        Assert.Equal(expected, PublishedCorrection.RewriteBy(origin));
    }
}
