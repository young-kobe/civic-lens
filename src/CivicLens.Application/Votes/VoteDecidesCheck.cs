using System.Text.RegularExpressions;
using CivicLens.Application.Actions;
using CivicLens.Core.Actions;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Votes;

/// <summary>
/// The one owner of the checks of what a vote decides. A failed check is an error that goes back to the model, and a draft that keeps failing writes
/// no text. The checks are: the quote is an exact span of the Congressional Research Service summary, every amount and year is in the text that the
/// model read, no number equals a vote count, no member is named, a result word equals the code result, and the text reads at or below
/// <see cref="ReadingGrade.MaximumGrade"/>. Code never reads the answer for any other meaning, so the instruction alone keeps the text on the bill.
/// </summary>
internal static partial class VoteDecidesCheck
{
    private static readonly Regex ResultWords = new(@"\b(?:" + string.Join('|', PlainVoteWords.AllResultWords.Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IEnumerable<string> Numbers(string text) =>
        Digits().Matches(text).Select(match => match.Value.Replace(",", string.Empty, StringComparison.Ordinal));

    public static VoteDecides? Check(VoteDecidesOutput? output, VoteDecidesInput? input, VoteSnapshot snapshot, List<string> errors)
    {
        if (input is null)
        {
            if (output is not null) errors.Add("This vote has no bill summary, so it takes no decides field.");
            return null;
        }
        if (output is null)
        {
            errors.Add("decides is required.");
            return null;
        }
        var before = errors.Count;
        CheckText(output.Text, VoteDecides.MaximumTextLength, "decides", required: true, errors);
        CheckConfidence(output.Confidence, "decides confidence", errors);
        if (errors.Count > before) return null;
        CheckQuote(output.Quote, input, errors);
        CheckFigures(output.Text, input, errors);
        CheckCounts(output.Text, input, errors);
        CheckNames(output.Text, snapshot, errors);
        CheckResultWords(output.Text, input, errors);
        CheckGrade(output.Text, errors);
        return errors.Count > before ? null : new VoteDecides(output.Text, output.Quote!, input.SourceHash);
    }

    private static void CheckQuote(string? quote, VoteDecidesInput input, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(quote))
        {
            errors.Add("The decides quote is required.");
            return;
        }
        if (quote.Length > VoteDecides.MaximumQuoteLength || quote.Contains('\n') || ActionTextCitation.WordCount(quote) < ActionDraftRevision.MinimumTextQuoteWords)
            errors.Add($"The decides quote has at least {ActionDraftRevision.MinimumTextQuoteWords} words, at most {VoteDecides.MaximumQuoteLength} characters, and no line break.");
        else if (!input.SummaryText.Contains(quote, StringComparison.Ordinal))
            errors.Add("The decides quote is not an exact part of the summary. Copy the characters exactly.");
    }

    private static void CheckFigures(string text, VoteDecidesInput input, List<string> errors)
    {
        var missing = new BillFigureSet(input.ReadText).Missing(text, input.BillNumber);
        if (missing.Count > 0)
            errors.Add($"The decides text states {string.Join(", ", missing.Select(Shorten))}, which the text you read does not hold. Use only the amounts and years that the text holds.");
    }

    private static void CheckCounts(string text, VoteDecidesInput input, List<string> errors)
    {
        if (Numbers(text).FirstOrDefault(input.CountNumbers.Contains) is { } number)
            errors.Add($"The decides text holds the number {number}, which is a vote count. Write no vote count. Code shows the counts.");
    }

    private static void CheckNames(string text, VoteSnapshot snapshot, List<string> errors)
    {
        foreach (var name in snapshot.PersonNames.Where(name => text.Contains(name, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"The decides text names \"{Shorten(name)}\". The list beside the text names the members and their votes, so name no person.");
    }

    private static void CheckResultWords(string text, VoteDecidesInput input, List<string> errors)
    {
        foreach (var word in ResultWords.Matches(text).Select(match => match.Value).Where(word => !word.Equals(input.ResultWord, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
            errors.Add(input.ResultWord is null
                ? $"The decides text says \"{word}\". Write no result word, because the result is \"{input.Result}\"."
                : $"The decides text says \"{word}\". The result is \"{input.ResultWord}\". Write that word or no result word.");
    }

    private static void CheckGrade(string text, List<string> errors)
    {
        var grade = ReadingGrade.Rounded(text);
        if (grade > ReadingGrade.MaximumGrade)
            errors.Add($"The decides text reads at grade {ReadingGrade.Format(grade)}. Write at grade {ReadingGrade.MaximumGrade:0} or below, with shorter sentences.");
    }

    [GeneratedRegex(@"\d+(?:,\d{3})*", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();
}
