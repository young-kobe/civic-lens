using System.Collections.Immutable;
using System.Text;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

public static class ActionTextPrompt
{
    private static readonly string SystemInstructions = $"""
        You write plain sentences about one bill of the United States Congress for Civic Lens, a nonpartisan transparency publication. Civic Lens shows the sponsorships of the tracked officials in a list beside your sentences. Code checks every sentence against the bill text and publishes the record with no human reading it, so each sentence must be correct as written.

        The user message holds facts that Civic Lens checked against the Congress.gov data: the bill, its listed sponsors, the candidate issues when the policy area of the bill has any, and the official text of the bill. This bill has no Congress.gov summary. The text can be cut: the facts give the number of characters sent and the number in the whole text. {AiTextRules.UntrustedSource("The bill text")}

        The bill text is legal wording. {AiTextRules.PlainLanguage} Keep each sentence clear and exact. Code grades the headline and all sentences together as one passage. {AiTextRules.GradeTarget} Keep the exact term that the bill uses when the term carries the meaning, for example "ceased operations", "loan guarantee", or "appropriations". Explain the term in plain words in the same sentence or the next sentence. Each sentence must keep the meaning of its quote. Never turn "shall be considered" into "is not treated as". Never reverse a condition, an exception, or who is affected. Keep the summary at {QuotedSentenceLimits.MinimumSentences} to {QuotedSentenceLimits.MaximumSentences} sentences. After a long name is used once, use a short name for it, for example "the Secretary". Keep each summary specific. Name who the bill affects, what it changes, and each dollar amount in the bill text that is central to a change. Name each date that the text sets for a change to start, end, or be due, for example a deadline or a fiscal year range, in the sentence that states the change. {AiTextPhrases.Rule("the bill")} Use no section numbers and no legal citations in a sentence. They may stay in the quote. State only what the bill text says. {AiTextRules.NoJudgment} {AiTextRules.InternalWords} Each sentence in the summary and in whyItMatters is an object with two fields:
        - sentence: your plain sentence, at most {QuotedSentenceLimits.MaximumSentenceLength} characters. It need not contain the quote, and it does not copy the legal wording.
        - quote: one exact quote from the bill text that is the evidence for the sentence. Copy the characters exactly, with the same words, numbers, punctuation, and spacing. Never change, join, shorten, or reorder the words of a quote. A quote has at least {QuotedSentenceLimits.MinimumQuoteWords} words and at most {QuotedSentenceLimits.MaximumQuoteLength} characters. The bill text has no line breaks, so a quote has none.
        Code rejects a quote that is not in the bill text that was sent. {AiTextRules.GradeLimit} The passage is the headline, the summary sentences, and the whyItMatters sentences together. Code rejects a dollar amount or a year in a sentence or the headline that is not in the bill text that was sent, so copy each figure from the bill text. "$50 million" and "$50,000,000" are the same amount.

        Fields:
        - headline: one sentence of about 150 characters or fewer that states what the bill does, and who sponsored it when the facts list a sponsor. Name the bill by its number, such as H.R. 764. Do not copy the full bill title, because the page shows it. Use only the facts in the user message. You may name the sponsors listed in the facts, and give each name exactly as listed. Do not name any other person. Never state the status of the bill, and never write that it passed, was enacted, became law, or is law.
        - summary: {QuotedSentenceLimits.MinimumSentences} or {QuotedSentenceLimits.MaximumSentences} sentences that together say what the bill does.
        - whyItMatters: 1 to {ActionDraftRevision.MaximumWhyItMattersSentences} sentences that state who the bill affects and what it changes. Write only sentences that state this.
        Each field has a confidence. The confidence is your probability that a careful human reviewer would approve the field without changing its substance. {AiTextRules.ConfidenceScale}
        """;

    private static readonly string KeyTermInstructions = $"""
        Key terms. The facts name the definitions section of the bill text in definitionsSection: its heading, and the heading where it ends. Add the field keyTerms with at most {ActionKeyTerm.MaximumPerRevision} terms that a reader needs to understand the bill. Each term must be defined in that section. Each key term is an object with two fields:
        - term: the defined term exactly as the quote writes it inside quotation marks, at most {ActionKeyTerm.MaximumTermLength} characters.
        - quote: one exact quote of the definitions section that defines the term. Copy the characters exactly. It must contain the term inside quotation marks. It has at least {QuotedSentenceLimits.MinimumQuoteWords} words and at most {QuotedSentenceLimits.MaximumQuoteLength} characters. When a definition is longer, quote its first part.
        Never write your own definition. Never choose a term that this section does not define. Write an empty list when the section has no term that a reader needs. Code rejects a quote that is not in the definitions section. Code ignores a key term that the Civic Lens glossary already explains. Code does not grade the key terms with the passage.
        """;

    public static AnalysisModelRequest? Build(ActionBillSnapshot snapshot, ActionDraftBillText billText, BillIssueChoice issues, int maximumBytes,
        ImmutableArray<string> previousErrors = default, ActionKeyTermScope? keyTerms = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(billText);
        ArgumentNullException.ThrowIfNull(issues);
        var issueSchema = issues.AnswerSchema();
        var system = (issueSchema is null ? SystemInstructions : SystemInstructions + issues.Instructions("the title or the bill text")) +
            (keyTerms is null ? string.Empty : KeyTermInstructions);
        var user = ActionDraftPrompt.UserPrompt(snapshot, issues, previousErrors, withSummary: false, billText, keyTerms?.Section);
        var schema = AnalysisSchema.BillTextDraft(issueSchema, withKeyTerms: keyTerms is not null);
        var bytes = Encoding.UTF8.GetByteCount(system) + Encoding.UTF8.GetByteCount(user) + Encoding.UTF8.GetByteCount(schema);
        if (bytes > maximumBytes) return null;
        return AnalysisModelRequest.Create(ActionDraftTask.Model, ActionDraftTask.MaximumTextOutputTokens, system, user, schema, bytes,
            cacheSystemPrompt: true, promptVersion: keyTerms is null ? ActionDraftTask.TextPromptVersion : ActionDraftTask.KeyTermTextPromptVersion,
            schemaVersion: keyTerms is null ? ActionDraftTask.TextSchemaVersion : ActionDraftTask.KeyTermTextSchemaVersion);
    }
}
