using System.Globalization;

namespace CivicLens.Core.ExecutiveActions;

/// <summary>Owns the ordered fact lines of a Federal Register presidential document. The extraction text and the revision both read it.</summary>
public static class ExecutiveActionFacts
{
    public const string DocumentNumber = "Federal Register document";
    public const string Type = "Document type";
    public const string PresidentialNumber = "Presidential document number";
    public const string Title = "Title";
    public const string Signed = "Signed";
    public const string Published = "Published";
    public const string Citation = "Citation";
    public const string President = "President";
    public const string CorrectionOf = "Corrects document";
    public const string Url = "Document URL";

    public static string Line(string label, string value) => $"{label}: {value}";

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static IReadOnlyList<(string Label, string Value)> Of(string documentNumber, string? correctionOf, ExecutiveActionKind kind,
        string? presidentialNumber, string title, DateOnly signedOn, DateOnly publishedOn, string citation, string presidentName, string url)
    {
        var facts = new List<(string, string)>
        {
            (DocumentNumber, documentNumber),
            (Type, kind.Label())
        };
        if (presidentialNumber is not null) facts.Add((PresidentialNumber, presidentialNumber));
        facts.Add((Title, title));
        facts.Add((Signed, Date(signedOn)));
        facts.Add((Published, Date(publishedOn)));
        facts.Add((Citation, citation));
        facts.Add((President, presidentName));
        if (correctionOf is not null) facts.Add((CorrectionOf, correctionOf));
        facts.Add((Url, url));
        return facts;
    }
}
