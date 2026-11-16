using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// The unbounded summary and source parts of one record, in index order, the summary that snippets come from, and the text origin and label column
/// of the view that holds the summary.
/// <c>Problem</c> is set when the stored rows of the record cannot give its texts. The record is then skipped and reported.
/// </summary>
public sealed record RecordTexts(string[] Summary, string[] Source, string? Snippet = null, string? Problem = null)
{
    public PublishedTextOrigin? TextOrigin { get; init; }
    public string? Label { get; init; }

    /// <summary>Builds the texts of one record. A row that breaks a rule fails that record only.</summary>
    public static RecordTexts Of(Func<RecordTexts> build)
    {
        try
        {
            return build();
        }
        catch (InvalidDataException exception)
        {
            return new([], [], Problem: exception.Message);
        }
    }
}
