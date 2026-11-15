using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.FeedStatements;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

/// <summary>
/// The one owner of the short release rule. A short release page shows its own text, so drafting makes no model call for it. Code copies
/// the saved text into the summary and its first line into the headline, and marks each quoted passage. The limit counts the words of the
/// saved text, because that is the exact text that the page shows: the title, the date line, and the body.
/// </summary>
public sealed class StatementSourceText
{
    /// <summary>The owner set about 120 words. A saved page region holds the title and the date line too, and the page shows them, so the
    /// limit counts them.</summary>
    public const int MaximumWords = 120;

    private static readonly char[] ClauseEnds = ['.', '!', '?', '\n', '\r', '"', '“', '”'];
    private static readonly string[] SaidWords = ["said", "says"];
    private const int MaximumNameWords = 4;

    // The titles that may come before the name in "said NAME". Any other word of NAME must be a word of the official's own name, so
    // "said Senator Tim Scott" never counts for Rick Scott. A title with a period, such as "Sen.", stops the clause before this check.
    private static readonly string[] TitleWords =
        ["Senator", "Representative", "Congressman", "Congresswoman", "Delegate", "Resident", "Commissioner", "Leader", "Whip", "Speaker", "Chair", "Chairman", "Chairwoman"];

    private readonly IReadOnlyList<SourceSpan> passages;

    private StatementSourceText(string headline, ImmutableArray<StatementQuotation> quotations, IReadOnlyList<SourceSpan> passages) =>
        (Headline, Quotations, this.passages) = (headline, quotations, passages);

    public string Headline { get; }

    public ImmutableArray<StatementQuotation> Quotations { get; }

    /// <summary>The source text of a short release page, or null when the model drafts it. A feed item starts with its feed lines, not
    /// with its title, so it keeps the model path. A first line that cannot be a headline also keeps it.</summary>
    public static StatementSourceText? Read(DocumentExtraction extraction)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        var text = extraction.Text;
        if (FeedStatementExtractions.IsFeedItem(extraction) || ActionTextCitation.WordCount(text) > MaximumWords ||
            text.Length > StatementDraftRevision.MaximumSummaryLength) return null;
        var headline = FirstLine(text);
        if (headline.Length is 0 or > StatementDraftRevision.MaximumHeadlineLength) return null;
        var passages = new QuotedText(text).Passages(extraction.ExtractionId);
        var quotations = QuotationsOf(extraction, headline, passages);
        return quotations.IsEmpty ? null : new(headline, quotations, passages);
    }

    /// <summary>The code revision. Its speaker is the official that code knows, when code can attribute every quoted passage of the text
    /// to that official. Otherwise the speaker is empty and a person chooses the speakers, because only a model or a person can name the
    /// other speakers. Code never guesses a date.</summary>
    public StatementDraftRevision Revision(StatementDraftEvidence evidence, AnalysisOfficial? speaker, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var text = evidence.Article.Extraction.Text;
        ImmutableArray<string> speakers = speaker is not null && IsOnlyQuoting(text, speaker.Name) ? [speaker.Id] : [];
        var revision = new StatementDraftRevision(evidence.Article.CandidateId, evidence.NextRevisionNumber, ReviewAuthor.CodeSubject,
            createdAtUtc, Headline, text, null, null, speakers, [], evidence.CarryOver.IssueIds, evidence.CarryOver.PolicyAreaIds, Quotations, []);
        return StatementDraftResolver.WithArticleDerivedFields(revision, text);
    }

    /// <summary>Code attributes a quoted passage only when the clause next to it is "said NAME" or "NAME said". NAME is one to four
    /// capitalized words that end with the last name of the official. Each other word is a title or a word of the official's name, so a
    /// clause that names another first name never counts. Every passage counts, also one that is not a quotation of the revision.
    /// A text with no quoted passage is release text of the office.</summary>
    private bool IsOnlyQuoting(string text, string officialName)
    {
        var nameWords = officialName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return nameWords.Length > 0 && passages.All(passage =>
            NamesOnly(ClauseAfter(text, passage), nameWords) || NamesOnly(ClauseBefore(text, passage), nameWords));
    }

    /// <summary>The text after the closing mark up to the end of its clause. A passage with no closing mark has no clause after it.</summary>
    private static string ClauseAfter(string text, SourceSpan passage)
    {
        var closingMark = passage.Start + passage.Length;
        if (closingMark >= text.Length || text[closingMark] is '\n' or '\r') return "";
        var end = text.IndexOfAny(ClauseEnds, closingMark + 1);
        return text[(closingMark + 1)..(end < 0 ? text.Length : end)];
    }

    /// <summary>The text from the start of its clause up to the opening mark.</summary>
    private static string ClauseBefore(string text, SourceSpan passage)
    {
        var openingMark = passage.Start - 1;
        var start = openingMark == 0 ? 0 : text.LastIndexOfAny(ClauseEnds, openingMark - 1) + 1;
        return text[start..openingMark];
    }

    private static bool NamesOnly(string clause, string[] nameWords)
    {
        var words = clause.Trim().Trim(',', ':').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 2) return false;
        var name = SaidWords.Contains(words[0], StringComparer.Ordinal) ? words[1..]
            : SaidWords.Contains(words[^1], StringComparer.Ordinal) ? words[..^1] : [];
        return name.Length is > 0 and <= MaximumNameWords && name.All(word => char.IsUpper(word[0])) &&
            string.Equals(name[^1], nameWords[^1], StringComparison.Ordinal) &&
            name[..^1].All(word => TitleWords.Contains(word, StringComparer.Ordinal) || nameWords.Contains(word, StringComparer.Ordinal));
    }

    /// <summary>Each quoted passage of the text, up to the revision limit. A text with no quoted passage cites its body as release text.</summary>
    private static ImmutableArray<StatementQuotation> QuotationsOf(DocumentExtraction extraction, string headline, IReadOnlyList<SourceSpan> passages)
    {
        var quoted = passages
            .Where(span => span.Length is > 0 and <= StatementDraftRevision.MaximumQuotationLength)
            .Take(StatementDraftRevision.MaximumQuotations).ToArray();
        if (quoted.Length > 0) return [.. quoted.Select(span => new StatementQuotation(span, StatementQuotationKind.DirectQuotation))];
        var body = BodySpan(extraction, headline);
        return body.Length is > 0 and <= StatementDraftRevision.MaximumQuotationLength
            ? [new StatementQuotation(body, StatementQuotationKind.ReleaseText)] : [];
    }

    /// <summary>The trimmed text after the headline line, or the headline line itself when nothing follows it.</summary>
    private static SourceSpan BodySpan(DocumentExtraction extraction, string headline)
    {
        var text = extraction.Text;
        var headlineStart = text.IndexOf(headline, StringComparison.Ordinal);
        var bodyStart = headlineStart + headline.Length;
        while (bodyStart < text.Length && char.IsWhiteSpace(text[bodyStart])) bodyStart++;
        var bodyEnd = text.Length;
        while (bodyEnd > bodyStart && char.IsWhiteSpace(text[bodyEnd - 1])) bodyEnd--;
        return bodyEnd > bodyStart ? new(extraction.ExtractionId, bodyStart, bodyEnd - bodyStart)
            : new(extraction.ExtractionId, headlineStart, headline.Length);
    }

    private static string FirstLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
}
