using System.Text.RegularExpressions;
using CivicLens.Application.Actions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class VoteSentencesReadingGradeTests
{
    private static readonly string[] Results = ["Passed", "Agreed to", "Confirmed", "Failed", "Not Agreed to", "Not Confirmed"];
    private static readonly string?[] BillTypes = [null, "hr", "s", "hjres", "sjres", "hconres", "sconres", "hres", "sres"];

    [Fact]
    public void EverySentenceOfTheVoteMeaningAndNextStepTablesReadsAtTheMaximumGradeOrBelowSoTheOwnerCanApproveWordsThatEveryReaderCanRead()
    {
        var sentences = MeaningTexts().Concat(NextStepTexts()).SelectMany(Sentences).Distinct().ToArray();

        Assert.True(sentences.Length > 40);
        var tooHard = sentences.Where(sentence => ReadingGrade.Rounded(sentence) > ReadingGrade.MaximumGrade).ToArray();
        Assert.True(tooHard.Length == 0, "Above grade " + ReadingGrade.MaximumGrade + ": " + string.Join(" | ", tooHard));
    }

    private static IEnumerable<string> MeaningTexts() =>
        Enum.GetValues<PublishedVoteKind>().Select(VoteMeaning.Of).OfType<(string Yes, string No)>().SelectMany(meaning => new[] { meaning.Yes, meaning.No });

    private static IEnumerable<string> NextStepTexts() =>
        from kind in Enum.GetValues<PublishedVoteKind>()
        from result in Results
        from chamber in Enum.GetValues<PublishedChamber>()
        from billType in BillTypes
        select NextStep.Of(kind, result, chamber, billType) into text
        where text is not null
        select text;

    private static IEnumerable<string> Sentences(string text) => Regex.Split(text, @"(?<=\.)\s+");
}
