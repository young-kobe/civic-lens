using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public static class PartyLetters
{
    public const string Independent = "Independent";

    public static string? NameOf(string letter) => letter switch
    {
        "R" => PublishedPartyMajority.Republican,
        "D" => PublishedPartyMajority.Democrat,
        "I" => Independent,
        _ => null
    };

    public static string? LetterOf(string name) => name switch
    {
        PublishedPartyMajority.Republican => "R",
        PublishedPartyMajority.Democrat => "D",
        Independent => "I",
        _ => null
    };
}
