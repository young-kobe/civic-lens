namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// The unbounded summary and source parts of one record, in index order, and the summary that snippets come from.
/// <c>Problem</c> is set when the stored rows of the record cannot give its texts. The record is then skipped and reported.
/// </summary>
public sealed record RecordTexts(string[] Summary, string[] Source, string? Snippet = null, string? Problem = null)
{
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
