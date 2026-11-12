using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.Collection.FederalRegister;

public sealed record FederalRegisterDocument(string DocumentNumber, string? CorrectionOf, ExecutiveActionKind Kind, string? PresidentialNumber,
    string Title, DateOnly SignedOn, DateOnly PublishedOn, string Citation, string PresidentSlug, string PresidentName, string Url)
{
    public string? DispositionNotes { get; init; }

    /// <summary>The <c>body_html_url</c> of the detail response. A job of an earlier pinned field list has none.</summary>
    public string? BodyHtmlUrl { get; init; }

    public string RecordDocumentNumber => CorrectionOf ?? DocumentNumber;
}
