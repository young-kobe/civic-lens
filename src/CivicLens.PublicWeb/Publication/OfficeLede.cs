using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The one owner of the lede rules for an office holder: the party word, the place, the caucus phrase, and when a term has ended.
/// The static official page and the server-rendered official page both call it, so a rule cannot differ between them.
/// </summary>
public static class OfficeLede
{
    /// <summary>The sentence start of a lede without its final period, such as "Former Democratic representative for WA, district 9". <paramref name="place"/> is <see cref="Place"/>.</summary>
    public static string Of(bool hasEnded, string? party, string role, string place, string? caucus)
    {
        var caucusPhrase = party is not null && CaucusWith(party, caucus) is { } with ? $", {with}" : "";
        var words = string.Join(' ', new[] { hasEnded ? "former" : null, party is null ? null : PartyAdjective(party), role }.OfType<string>());
        return char.ToUpperInvariant(words[0]) + words[1..] + place + caucusPhrase;
    }

    /// <summary>The place of a seat, such as " for Massachusetts, district 7". It is empty when the seat has no state.</summary>
    public static string Place(string? state, int? district) => (state, district) switch
    {
        (null, _) => "",
        (_, { } number) => $" for {state}, district {number}",
        _ => $" for {state}"
    };

    /// <summary>The phrase "caucuses with Democrats" when the caucus differs from the party, otherwise null.</summary>
    public static string? CaucusWith(string party, string? caucus) =>
        caucus is null || caucus == party ? null : $"caucuses with {PublishedPartyNames.Members(caucus)}";

    /// <summary>A term has ended when its end day is before the day of the release.</summary>
    public static bool HasEnded(DateOnly? end, DateOnly publishedOn) => end is { } day && day < publishedOn;

    private static string PartyAdjective(string party) => party == PublishedPartyMajority.Democrat ? "Democratic" : party;
}
