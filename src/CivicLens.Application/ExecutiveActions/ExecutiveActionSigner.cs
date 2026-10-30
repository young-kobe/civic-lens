using CivicLens.Application.Collection;
using CivicLens.Application.Collection.FederalRegister;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>Finds the configured President who signed a document. The person's ID must equal the President that the Federal Register cites.</summary>
public static class ExecutiveActionSigner
{
    private const string PresidentTitle = "President";

    public static string PersonIdOf(CollectionConfiguration configuration, FederalRegisterDocument document)
    {
        var inOffice = configuration.People.Where(person => IsPresidentOn(person, document.SignedOn)).ToArray();
        if (inOffice.Length == 0)
            throw new InvalidDataException("No configured President held office on the signing date of the document.");
        return inOffice.SingleOrDefault(person => person.Id == document.PresidentSlug)?.Id
            ?? throw new InvalidDataException("The President that the Federal Register cites is not a configured President in office on the signing date.");
    }

    private static bool IsPresidentOn(PersonConfiguration person, DateOnly day) =>
        person.Offices?.Any(office => office.Title == PresidentTitle && office.StartsOn <= day && (office.EndsOn is null || day <= office.EndsOn)) == true;
}
