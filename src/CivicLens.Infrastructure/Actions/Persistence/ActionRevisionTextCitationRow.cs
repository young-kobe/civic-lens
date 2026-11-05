using CivicLens.Core.Actions;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionTextCitationRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public ActionTextPart Part { get; set; }
    public string CitationExtractionId { get; set; } = string.Empty;
    public int CitationStart { get; set; }
    public int CitationLength { get; set; }
    public string Sentence { get; set; } = string.Empty;
}
