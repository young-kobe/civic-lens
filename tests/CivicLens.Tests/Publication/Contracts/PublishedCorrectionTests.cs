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
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(2), Entry(2)], 3));
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll([Entry(1), Entry(2)], 3));
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
    public void RejectsMoreThanTheMaximumEntries()
    {
        var list = Enumerable.Range(1, PublishedCorrection.MaximumEntries + 1).Reverse().Select(number => Entry(number)).ToArray();

        PublishedCorrection.ValidateAll(list[1..], 30);
        Assert.Throws<InvalidDataException>(() => PublishedCorrection.ValidateAll(list, 30));
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
