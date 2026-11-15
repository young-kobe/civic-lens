using System.Collections.Immutable;
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

    private StatementSourceText(string headline, ImmutableArray<StatementQuotation> quotations) =>
        (Headline, Quotations) = (headline, quotations);

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
        var quotations = QuotationsOf(extraction, headline);
        return quotations.IsEmpty ? null : new(headline, quotations);
    }

    /// <summary>The code revision. Its speaker is the office owner of the source, or the one covered official. With several officials
    /// and no office owner, the speaker is empty and a person chooses it. Code never guesses a date.</summary>
    public StatementDraftRevision Revision(StatementDraftEvidence evidence, ImmutableArray<string> speakers, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var text = evidence.Article.Extraction.Text;
        var revision = new StatementDraftRevision(evidence.Article.CandidateId, evidence.NextRevisionNumber, ReviewAuthor.CodeSubject,
            createdAtUtc, Headline, text, null, null, speakers, [], evidence.CarryOver.IssueIds, evidence.CarryOver.PolicyAreaIds, Quotations, []);
        return StatementDraftResolver.WithArticleDerivedFields(revision, text);
    }

    /// <summary>Each quoted passage of the text, up to the revision limit. A text with no quoted passage cites its body as release text.</summary>
    private static ImmutableArray<StatementQuotation> QuotationsOf(DocumentExtraction extraction, string headline)
    {
        var passages = new QuotedText(extraction.Text).Passages(extraction.ExtractionId)
            .Where(span => span.Length is > 0 and <= StatementDraftRevision.MaximumQuotationLength)
            .Take(StatementDraftRevision.MaximumQuotations).ToArray();
        if (passages.Length > 0) return [.. passages.Select(span => new StatementQuotation(span, StatementQuotationKind.DirectQuotation))];
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
