using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedCorrectionTests
{
    private static PublishedCorrection Entry(int replaced, string? note = null) =>
        new() { ReplacedRevisionNumber = replaced, PublishedAtUtc = DateTimeOffset.UnixEpoch, Note = note };

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
    public void AcceptsCodeUpdatesBetweenNotedEntriesSoTheListHoldsAtMostFortyOne()
    {
        var list = Enumerable.Range(1, 41).Reverse().Select(number => Entry(number, number % 2 == 0 ? "Fix." : null)).ToArray();

        PublishedCorrection.ValidateAll(list, 42);
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
