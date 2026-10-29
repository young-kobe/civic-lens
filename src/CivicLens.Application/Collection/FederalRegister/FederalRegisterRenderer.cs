using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.Collection.FederalRegister;

public static class FederalRegisterRenderer
{
    public static string Render(FederalRegisterDocument document) =>
        string.Join('\n', ExecutiveActionFacts.Of(document.DocumentNumber, document.CorrectionOf, document.Kind, document.PresidentialNumber,
            document.Title, document.SignedOn, document.PublishedOn, document.Citation, document.PresidentName, document.Url)
            .Select(fact => ExecutiveActionFacts.Line(fact.Label, fact.Value)));
}
