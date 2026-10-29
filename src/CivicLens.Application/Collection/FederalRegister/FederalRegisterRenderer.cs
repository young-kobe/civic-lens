using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.Collection.FederalRegister;

public static class FederalRegisterRenderer
{
    public static string Render(FederalRegisterDocument document)
    {
        var lines = ExecutiveActionFacts.Of(document.DocumentNumber, document.Kind, document.PresidentialNumber,
            document.Title, document.SignedOn, document.PublishedOn, document.Citation, document.PresidentName, document.Url)
            .Select(fact => ExecutiveActionFacts.Line(fact.Label, fact.Value));
        return string.Join('\n', document.CorrectionOf is null ? lines : lines.Append(NoticeLine(document)));
    }

    public static string NoticeLine(FederalRegisterDocument document) =>
        ExecutiveActionFacts.Line(ExecutiveActionFacts.CorrectionNotice,
            ExecutiveActionFacts.NoticeValue(document.DocumentNumber, document.PublishedOn, document.Url));
}
