using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Bills;

/// <summary>One row of the view <c>public_api.bills</c>.</summary>
public sealed record BillRow
{
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required int Congress { get; init; }
    public required string BillType { get; init; }
    public required int BillNumber { get; init; }
    public required string BillTitle { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required PublishedTextOrigin TextOrigin { get; init; }
    public string? Label { get; init; }
    public string? PolicyArea { get; init; }
    public string? PolicyAreaId { get; init; }
    public required DateOnly EventDate { get; init; }
    public required int SponsorCount { get; init; }
    public required int CosponsorCount { get; init; }
    public string? BillLine { get; init; }
    public PublishedTextOrigin? BillLineOrigin { get; init; }
    public string? BillLineLabel { get; init; }

    /// <summary>The "why it matters" text of an AI summary of the bill text. It is null unless <see cref="TextOrigin"/> is Model and the bill has a text summary.</summary>
    public string? WhyItMatters { get; init; }

    /// <summary>The name of the bill text version that a text summary cites, such as "Introduced in House". Null when the summary cites no bill text.</summary>
    public string? TextVersionName { get; init; }
    public DateOnly? TextVersionDate { get; init; }

    /// <summary>The characters of that version that the model read, and all the characters of that version.</summary>
    public int? TextCharsRead { get; init; }
    public int? TextCharsTotal { get; init; }
}
