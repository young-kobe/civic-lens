using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.SenateVotes;

internal static partial class SenateXml
{
    private const string DateTimeFormat = "MMMM d, yyyy, h:mm tt";
    private const string DayFormat = "d-MMM yyyy";

    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static XElement Load(ReadOnlySpan<byte> xml, string name, string rootName)
    {
        try
        {
            using var stream = new MemoryStream(xml.ToArray());
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var root = XDocument.Load(reader).Root;
            return root is not null && root.Name.LocalName == rootName
                ? root
                : throw new InvalidDataException($"{name} must have a {rootName} root.");
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"{name} is not valid XML.", exception);
        }
    }

    public static VoteSession ReadSession(XElement root, string name) =>
        VoteSession.TryRead(Required(root, "congress", name), Required(root, "session", name))
        ?? throw new InvalidDataException($"{name} has no valid Congress and session.");

    public static SenateVoteRoll ReadRoll(VoteSession session, string number, string name) =>
        int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1
            ? new SenateVoteRoll(session, parsed)
            : throw new InvalidDataException($"{name} has an invalid vote number.");

    public static string Required(XElement parent, string child, string name) =>
        Optional(parent, child, name) ?? throw new InvalidDataException($"{name} must have a {child} element with text.");

    public static string? Optional(XElement parent, string child, string name)
    {
        var matches = parent.Elements(child).ToArray();
        if (matches.Length > 1) throw new InvalidDataException($"{name} has more than one {child} element.");
        return matches.SingleOrDefault() is { } element && Whitespace().Replace(element.Value, " ").Trim() is { Length: > 0 } text
            ? text
            : null;
    }

    public static int? OptionalCount(XElement parent, string child, string name) =>
        Optional(parent, child, name) is { } text
            ? ApiPageQuery.TryReadNumber(text) ?? throw new InvalidDataException($"{name} has an invalid {child} count.")
            : null;

    public static DateOnly ReadDay(string text, int year, string name) =>
        DateOnly.TryParseExact($"{text} {year}", DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new InvalidDataException($"{name} has an invalid vote date.");

    public static DateTimeOffset ReadDateTime(string text, string name)
    {
        if (!DateTime.TryParseExact(text, DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new InvalidDataException($"{name} has an invalid date and time.");
        return new DateTimeOffset(local, Eastern.GetUtcOffset(local));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
