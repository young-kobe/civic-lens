using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.Collection.FederalRegister;

public sealed record FederalRegisterDocument(string DocumentNumber, string? CorrectionOf, ExecutiveActionKind Kind, string? PresidentialNumber,
    string Title, DateOnly SignedOn, DateOnly PublishedOn, string Citation, string PresidentSlug, string PresidentName, string Url)
{
    public string? DispositionNotes { get; init; }

    public string RecordDocumentNumber => CorrectionOf ?? DocumentNumber;
}
