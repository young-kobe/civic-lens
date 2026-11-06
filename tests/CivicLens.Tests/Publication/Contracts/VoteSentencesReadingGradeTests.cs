using System.Text.RegularExpressions;
using CivicLens.Application.Actions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class VoteSentencesReadingGradeTests
{
    private static readonly string[] PageHeadings = [VoteMeaning.Heading, NextStep.Heading];

    [Fact]
    public void EverySentenceOfTheVoteMeaningAndNextStepTablesReadsAtTheMaximumGradeOrBelowSoTheOwnerCanApproveWordsThatEveryReaderCanRead()
    {
        var sentences = MeaningTexts().Concat(NextStepTexts()).Concat(PageHeadings).SelectMany(Sentences).Distinct().ToArray();

        Assert.True(sentences.Length > 40);
        var tooHard = sentences.Where(sentence => ReadingGrade.Rounded(sentence) > ReadingGrade.MaximumGrade).ToArray();
        Assert.True(tooHard.Length == 0, "Above grade " + ReadingGrade.MaximumGrade + ": " + string.Join(" | ", tooHard));
    }

    private static IEnumerable<string> MeaningTexts() =>
        Enum.GetValues<PublishedVoteKind>().Select(VoteMeaning.Of).OfType<(string Yes, string No)>().SelectMany(meaning => new[] { meaning.Yes, meaning.No });

    private static IEnumerable<string> NextStepTexts() => VoteWordingInputs.NextSteps().Select(step => step.Text).Append(NextStep.NotShown);

    private static IEnumerable<string> Sentences(string text) => Regex.Split(text, @"(?<=\.)\s+");
}
