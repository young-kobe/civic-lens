using CivicLens.Core.Actions;
using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.Collection.FederalRegister;

public static class FederalRegisterRenderer
{
    public static string Render(FederalRegisterDocument document)
    {
        var lines = ExecutiveActionFacts.Of(document.DocumentNumber, document.Kind, document.PresidentialNumber,
            document.Title, document.SignedOn, document.PublishedOn, document.Citation, document.PresidentName, document.Url)
            .Select(fact => ExecutiveActionFacts.Line(fact.Label, fact.Value));
        if (document.DispositionNotes is not null)
            lines = lines.Append(ExecutiveActionFacts.Line(ExecutiveActionFacts.DispositionNotes, document.DispositionNotes));
        return string.Join('\n', document.CorrectionOf is null ? lines : lines.Append(NoticeLine(document)));
    }

    /// <summary>The header names the document and the characters read. The text keeps the first <see cref="TextWindow.Length"/> characters.
    /// Empty text is invalid data.</summary>
    public static FederalRegisterTextReading RenderText(string documentNumber, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) throw new InvalidDataException("Document text is empty.");
        var window = TextWindow.Of(text);
        var lines = new[]
        {
            ExecutiveActionFacts.Line(ExecutiveActionFacts.DocumentNumber, documentNumber),
            RenderedText.Line("Characters read", $"{RenderedText.Count(window.Length)} of {RenderedText.Count(text.Length)}"),
            BillTextBody.Label,
            window
        };
        return new FederalRegisterTextReading(string.Join('\n', lines), window.Length, text.Length);
    }

    public static string NoticeLine(FederalRegisterDocument document) =>
        ExecutiveActionFacts.Line(ExecutiveActionFacts.CorrectionNotice,
            ExecutiveActionFacts.NoticeValue(document.DocumentNumber, document.PublishedOn, document.Url));
}
