using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The fixed words that the statement, executive action, and document change pages share. Each page owns its own words in its own view.</summary>
public static class DetailWords
{
    public const string CheckSource = "Check the source";

    public const string WhoReviewed = "Who reviewed it";

    public const string SourceSeen = "Source seen";

    /// <summary>The public label of model text, or null when a person or no one wrote the text. Code text has its own shared label.</summary>
    public static string? SummaryLabel(PublishedTextOrigin origin, string modelLabel) => origin switch
    {
        PublishedTextOrigin.Model => modelLabel,
        PublishedTextOrigin.Code => PublishedRecordLabels.CodeWording,
        _ => null
    };
}
