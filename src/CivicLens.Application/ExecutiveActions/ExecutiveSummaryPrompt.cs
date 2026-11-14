using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>The prompt of the executive action summary. It follows <see cref="ActionTextPrompt"/>: the same grade, quote, and figure rules,
/// and a text that can be cut. <see cref="ExecutiveSummaryResolver"/> checks each figure with <see cref="BillFigureSet"/>. The phrase rule is new. It asks for what the document does, and the closed list of <see cref="ExecutiveSummaryPhrases"/> backs it in the repair pass.</summary>
public static class ExecutiveSummaryPrompt
{
    private static readonly string SystemInstructions = $"""
        You write plain sentences about one presidential document of the United States for Civic Lens, a nonpartisan transparency publication. The document is an executive order, a proclamation, a memorandum, or a notice. Code checks every sentence against the document text and publishes the record, so each sentence must be correct as written.

        The user message holds facts that Civic Lens checked against the Federal Register: the title, kind, dates, and citation of the document, and its text. The text can be cut: the facts give the number of characters sent and the number in the whole document. When the text is cut, write only about the part that you read. {AiTextRules.UntrustedSource("The document text")}

        {AiTextRules.PlainLanguage} Keep each sentence clear and exact. Code grades all sentences together as one passage. {AiTextRules.GradeTarget} Keep the exact term that the document uses when the term carries the meaning, and explain the term in plain words in the same sentence or the next sentence. Write {QuotedSentenceLimits.MinimumSentences} to {QuotedSentenceLimits.MaximumSentences} sentences. After a long name is used once, use a short name for it.
        Say what the document does. Write who it orders, directs, or names, what each must do, and each date or dollar amount that the text sets. Do not write why the document was issued, and do not write what it hopes to achieve. Never use the phrases "aims to", "seeks to", or "intended to". Use no section numbers and no legal citations in a sentence. {AiTextRules.NoJudgment} {AiTextRules.InternalWords} Name the President only as "the President".
        Each sentence is an object with two fields:
        - sentence: your plain sentence, at most {QuotedSentenceLimits.MaximumSentenceLength} characters.
        - quote: one exact quote from the document text that is the evidence for the sentence. Copy the characters exactly, with the same words, numbers, punctuation, and spacing. Never change, join, shorten, or reorder the words of a quote. A quote has at least {QuotedSentenceLimits.MinimumQuoteWords} words and at most {QuotedSentenceLimits.MaximumQuoteLength} characters. A quote holds no line break, so it lies inside one line of the text.
        Code rejects a quote that is not in the text that was sent. {AiTextRules.GradeLimit} Code rejects a dollar amount or a year in a sentence that is not in the document text that was sent, so copy each figure from the document text. "$50 million" and "$50,000,000" are the same amount. Code also rejects a sentence with one of the three phrases above.
        The field summary holds the list of sentences and a confidence. The confidence is your probability that a careful human reviewer would approve the summary without changing its substance. {AiTextRules.ConfidenceScale}
        """;

    public static long MinimumReservation => Encoding.UTF8.GetByteCount(SystemInstructions) + ExecutiveSummaryTask.MaximumOutputTokens;

    public static AnalysisModelRequest? Build(ExecutiveSummaryEvidence evidence, int maximumBytes, ImmutableArray<string> previousErrors = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var user = UserPrompt(evidence, previousErrors);
        var schema = AnalysisSchema.ExecutiveSummary();
        var bytes = Encoding.UTF8.GetByteCount(SystemInstructions) + Encoding.UTF8.GetByteCount(user) + Encoding.UTF8.GetByteCount(schema);
        if (bytes > maximumBytes) return null;
        return AnalysisModelRequest.Create(ExecutiveSummaryTask.Model, ExecutiveSummaryTask.MaximumOutputTokens, SystemInstructions, user, schema, bytes,
            cacheSystemPrompt: true, promptVersion: ExecutiveSummaryTask.PromptVersion, schemaVersion: ExecutiveSummaryTask.SchemaVersion);
    }

    public static string SerializeContext(ExecutiveSummaryEvidence evidence) =>
        JsonSerializer.Serialize(new
        {
            documentNumber = evidence.Current.DocumentNumber,
            extractionId = evidence.Text.ExtractionId,
            charsRead = evidence.Text.CharsRead,
            charsTotal = evidence.Text.CharsTotal,
            revision = evidence.Current.RevisionNumber
        }, AnalysisPromptText.CompactJson);

    private static string UserPrompt(ExecutiveSummaryEvidence evidence, ImmutableArray<string> previousErrors)
    {
        var revision = evidence.Current;
        var document = new
        {
            document = new
            {
                number = revision.DocumentNumber,
                kind = revision.Kind.ToString(),
                presidentialNumber = revision.PresidentialNumber,
                title = revision.Title,
                signedOn = revision.SignedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                publishedOn = revision.PublishedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                citation = revision.FederalRegisterCitation
            },
            text = new { charactersSent = evidence.Text.Body.Length, charactersInDocument = evidence.Text.CharsTotal, cut = evidence.Text.Cut, body = evidence.Text.Body }
        };
        var builder = new StringBuilder("Facts:\n").Append(JsonSerializer.Serialize(document, AnalysisPromptText.EvidenceJson));
        return AnalysisPromptText.WithRejectionReasons(builder, previousErrors);
    }
}
