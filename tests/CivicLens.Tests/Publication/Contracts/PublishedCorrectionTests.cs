using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedCorrectionTests
{
    private static PublishedCorrection Entry(int replaced, string? note = null, bool factsUnchanged = false) =>
        new() { ReplacedRevisionNumber = replaced, PublishedAtUtc = DateTimeOffset.UnixEpoch, Note = note, FactsUnchanged = factsUnchanged };

    [Fact]
    public void AFactsUnchangedUpdateAndASourceUpdateKeepTheirOwnEntriesNextToEachOther()
    {
        PublishedCorrection.ValidateAll([Entry(2, factsUnchanged: true), Entry(1)], 3);
        PublishedCorrection.ValidateAll([Entry(2), Entry(1, factsUnchanged: true)], 3);
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(2, factsUnchanged: true), Entry(1, factsUnchanged: true)], 3));
    }

    [Fact]
    public void ARunBetweenPersonCorrectionsHoldsAtMostOneCodeUpdateOfEachKind()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(3), Entry(2, factsUnchanged: true), Entry(1)], 4));
        PublishedCorrection.ValidateAll([Entry(4), Entry(3, factsUnchanged: true), Entry(2, "Fix."), Entry(1)], 5);
    }

    [Fact]
    public void ANotedEntryCannotClaimThatTheFactsDidNotChange()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, "Fix.", factsUnchanged: true)], 2));
    }

    [Fact]
    public void PrependKeepsOneCodeUpdateOfEachKindInTheNewestRun()
    {
        var textOnly = Entry(2, factsUnchanged: true);

        Assert.Equal([textOnly], PublishedCorrection.Prepend([Entry(1, factsUnchanged: true)], textOnly));
        Assert.Equal([textOnly, Entry(1)], PublishedCorrection.Prepend([Entry(1)], textOnly));
        Assert.Equal([Entry(3), textOnly], PublishedCorrection.Prepend([textOnly, Entry(1)], Entry(3)));
        Assert.Equal([Entry(3), Entry(2, "Fix.")], PublishedCorrection.Prepend([Entry(2, "Fix.")], Entry(3)));
    }

    [Fact]
    public void AnOlderRecordFileWithoutTheFieldReadsAsASourceUpdateAndAPlainEntryWritesNoField()
    {
        const string older = """{"replacedRevisionNumber":1,"publishedAtUtc":"2026-02-02T09:00:00+00:00"}""";

        var read = JsonSerializer.Deserialize<PublishedCorrection>(older, PublicationProtocol.JsonOptions)!;

        Assert.False(read.FactsUnchanged);
        Assert.DoesNotContain("factsUnchanged", JsonSerializer.Serialize(read, PublicationProtocol.JsonOptions));
        Assert.Contains("\"factsUnchanged\":true", JsonSerializer.Serialize(Entry(1, factsUnchanged: true), PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void AcceptsEntriesFromTheNewestReplacedRevisionDown()
    {
        PublishedCorrection.ValidateAll([Entry(4, "Fixed a date."), Entry(2), Entry(1, "Typo.")], 5);
    }

    [Fact]
    public void RejectsEntriesThatAreNotStrictlyDecreasingSoTheListShowsOneHistory()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(2, "a"), Entry(2, "b")], 3));
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, "a"), Entry(2, "b")], 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    public void RejectsAReplacedRevisionThatIsNotAnEarlierRevisionOfTheRecord(int replaced)
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(replaced)], 3));
    }

    [Fact]
    public void RejectsMoreThanTheMaximumNotedEntries()
    {
        var list = Enumerable.Range(1, PublishedCorrection.MaximumNotedEntries + 1).Reverse().Select(number => Entry(number, "Fix.")).ToArray();

        PublishedCorrection.ValidateAll(list[1..], 30);
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll(list, 30));
    }

    [Fact]
    public void RejectsTwoCodeUpdatesNextToEachOtherBecauseARunOfThemIsOneEntry()
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(2), Entry(1)], 3));
    }

    [Fact]
    public void AcceptsTwoCodeUpdatesAroundEachNotedEntrySoTheListHoldsAtMostSixtyTwo()
    {
        var list = new List<PublishedCorrection>();
        var number = 62;
        for (var gap = 0; gap <= PublishedCorrection.MaximumNotedEntries; gap++)
        {
            list.Add(Entry(number--, factsUnchanged: true));
            list.Add(Entry(number--));
            if (gap < PublishedCorrection.MaximumNotedEntries) list.Add(Entry(number--, "Fix."));
        }

        PublishedCorrection.ValidateAll([.. list], 63);
        Assert.Equal(62, list.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0note")]
    public void RejectsABlankOrNulNoteBecauseANullNoteAlreadyMeansNoNote(string note)
    {
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1, note)], 2));
    }

    [Fact]
    public void RejectsANoteOverTheMaximumLength()
    {
        PublishedCorrection.ValidateAll([Entry(1, new string('a', PublishedCorrection.MaximumNoteLength))], 2);
        Assert.Throws<InvalidDataException>(() =>
            PublishedCorrection.ValidateAll([Entry(1, new string('a', PublishedCorrection.MaximumNoteLength + 1))], 2));
    }
}
