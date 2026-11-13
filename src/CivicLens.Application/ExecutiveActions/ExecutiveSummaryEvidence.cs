using CivicLens.Core.Actions;
using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>What the step reads for one record: its current revision, the stored body text, and whether a person rejected the record.</summary>
public sealed record ExecutiveSummaryEvidence(ExecutiveActionRevision Current, ExecutiveSummaryText Text, bool IsRejected);

/// <summary>The body text of an original document. <see cref="Text"/> is the whole extraction, with the header lines before the body.
/// The counts come from the stored text row. <see cref="Cut"/> is true when the extraction keeps only the first window of the document.</summary>
public sealed record ExecutiveSummaryText(string ExtractionId, string ExtractionVersion, string Text, int CharsRead, int CharsTotal)
{
    public int BodyStart => BillTextBody.Start(Text);

    public string Body => Text[BodyStart..];

    public bool Cut => CharsRead < CharsTotal;
}
