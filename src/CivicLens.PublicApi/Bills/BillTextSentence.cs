using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Bills;

/// <summary>One sentence of the AI text summary of a bill, from <c>public_api.bill_text_sentences</c>. <c>Part</c> tells a summary sentence from a
/// "why it matters" sentence. <c>Quote</c> holds the exact cited characters of the bill text that support the sentence. The AI label is the label of the bill.</summary>
public sealed record BillTextSentence(PublishedTextPart Part, string Sentence, string Quote);
