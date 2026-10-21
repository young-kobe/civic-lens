using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record RecordNote(PublishedRecordKind Kind, string RecordId, string Note)
{
    public PublishedRecordReference Reference => new(Kind, RecordId);

    internal string ValidText(string parameterName)
    {
        var text = Note?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() ?? string.Empty;
        return PublishedCorrection.IsNote(text)
            ? text
            : throw new ArgumentException($"A note has 1 to {PublishedCorrection.MaximumNoteLength} characters and no NUL.", parameterName);
    }
}
