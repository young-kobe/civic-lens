using System.Xml.Linq;
using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.SenateVotes;

public static class SenateVoteMenuParser
{
    public const int MaximumVotes = 999;

    private const string Name = "Senate vote menu";

    public static SenateVoteMenu Parse(ReadOnlySpan<byte> xml, VoteSession expected)
    {
        var root = SenateXml.Load(xml, Name, "vote_summary");
        var session = SenateXml.ReadSession(root, Name);
        if (session != expected) throw new InvalidDataException("Senate vote menu is for another session than requested.");
        var year = ReadYear(root, session);
        var list = root.Element("votes") ?? throw new InvalidDataException("Senate vote menu must contain votes.");
        var votes = list.Elements("vote").Select(item => ReadItem(item, session, year)).ToArray();
        if (votes.Length > MaximumVotes) throw new InvalidDataException("Senate vote menu holds more votes than the admission limit.");
        if (votes.Select(vote => vote.Roll.Number).Distinct().Count() != votes.Length)
            throw new InvalidDataException("Senate vote menu lists a vote number twice.");
        return new SenateVoteMenu(session, votes);
    }

    private static int ReadYear(XElement root, VoteSession session) =>
        ApiPageQuery.TryReadNumber(SenateXml.Required(root, "congress_year", Name)) is { } year && year == session.Year
            ? year
            : throw new InvalidDataException("Senate vote menu year does not match its session.");

    private static SenateVoteMenuItem ReadItem(XElement item, VoteSession session, int year)
    {
        var text = SenateXml.Required(item, "vote_date", Name);
        var day = SenateXml.ReadDay(text, year, Name);
        var endsNextYear = day is { Month: 1, Day: <= 2 };
        return new(SenateXml.ReadRoll(session, SenateXml.Required(item, "vote_number", Name), Name),
            endsNextYear ? day.AddYears(1) : day);
    }
}
