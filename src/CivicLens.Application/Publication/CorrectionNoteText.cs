using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal static class CorrectionNoteText
{
    public static string Validate(string? note, string parameterName)
    {
        var text = note?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() ?? string.Empty;
        return PublishedCorrection.IsNote(text)
            ? text
            : throw new ArgumentException($"A note has 1 to {PublishedCorrection.MaximumNoteLength} characters and no NUL.", parameterName);
    }
}
