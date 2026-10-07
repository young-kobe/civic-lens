namespace CivicLens.Core.Documents;

public sealed class DocumentTextSpan
{
    public DocumentTextSpan(DocumentExtraction extraction, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (start > extraction.Text.Length - length)
            throw new ArgumentException("Span must fit within the extracted text.", nameof(length));
        if (SplitsSurrogatePair(extraction.Text, start) || SplitsSurrogatePair(extraction.Text, start + length))
            throw new ArgumentException("Span boundaries cannot split a surrogate pair.");

        ExtractionId = extraction.ExtractionId;
        Start = start;
        Length = length;
        Quote = extraction.Text.Substring(start, length);
    }

    public string ExtractionId { get; }
    public int Start { get; }
    public int Length { get; }
    public string Quote { get; }

    private static bool SplitsSurrogatePair(string text, int boundary) => boundary > 0 && boundary < text.Length &&
        char.IsHighSurrogate(text[boundary - 1]) && char.IsLowSurrogate(text[boundary]);
}
