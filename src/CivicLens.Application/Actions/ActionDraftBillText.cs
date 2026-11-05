using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

/// <summary>The bill text that the gate of a candidate names. Text holds the whole extraction, with the header lines before the body.
/// The counts come from the stored text row, so they are not copied anywhere else.</summary>
public sealed record ActionDraftBillText(string PackageId, string VersionType, DateOnly? VersionDate, string ExtractionId,
    string ExtractionVersion, string Text, int CharsRead, int CharsTotal)
{
    public int BodyStart => BillTextBody.Start(Text);

    public string Body => Text[BodyStart..];
}
