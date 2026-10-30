using System.Xml.Linq;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Application.Collection.SenateVotes;

public static class SenateVoteParser
{
    public const string ExtractionVersion = "congress-senate-vote-v1";

    private const string Name = "Senate vote";

    public static SenateVoteDetail Parse(ReadOnlySpan<byte> xml, SenateVoteRoll expected)
    {
        var root = BoundedXml.LoadRoot(xml, Name, "roll_call_vote");
        var session = SenateXml.ReadSession(root, Name);
        if (SenateXml.ReadRoll(session, SenateXml.Required(root, "vote_number", Name), Name) != expected)
            throw new InvalidDataException("Senate vote is for another roll call than requested.");
        var document = root.Element("document");
        var (billType, billNumber) = ReadBill(document);
        var vote = new SenateVote(expected, SenateXml.ReadDateTime(SenateXml.Required(root, "modify_date", Name), Name),
            billType, billNumber);
        var counts = ReadCounts(root);
        var members = ReadMembers(root.Element("members"));
        RequireCountsMatch(counts, members);
        return new SenateVoteDetail(vote, SenateXml.ReadDateTime(SenateXml.Required(root, "vote_date", Name), Name),
            SenateXml.Required(root, "question", Name), SenateXml.Required(root, "vote_question_text", Name),
            SenateXml.Required(root, "vote_title", Name), SenateXml.Required(root, "vote_result", Name),
            SenateXml.Optional(root, "vote_result_text", Name), SenateXml.Optional(root, "vote_document_text", Name),
            SenateXml.Optional(root, "majority_requirement", Name),
            document is null ? null : SenateXml.Optional(document, "document_name", Name), counts, members);
    }

    private static (string? Type, int? Number) ReadBill(XElement? document)
    {
        if (document is null || SenateXml.Optional(document, "document_type", Name) is not { } citationType ||
            CongressBillListItem.NormalizeCitationType(citationType) is not { } type)
            return (null, null);
        var number = ApiPageQuery.TryReadNumber(SenateXml.Required(document, "document_number", Name));
        return number is >= 1 ? (type, number) : throw new InvalidDataException("Senate vote bill number is invalid.");
    }

    private static SenateVoteCounts ReadCounts(XElement root)
    {
        var count = root.Element("count") ?? throw new InvalidDataException("Senate vote must contain count.");
        return new SenateVoteCounts(SenateXml.OptionalCount(count, "yeas", Name), SenateXml.OptionalCount(count, "nays", Name),
            SenateXml.OptionalCount(count, "present", Name), SenateXml.OptionalCount(count, "absent", Name));
    }

    private static void RequireCountsMatch(SenateVoteCounts counts, SenateVoteMember[] members)
    {
        var expected = new Dictionary<string, int>
        {
            ["Yea"] = counts.Yeas ?? 0,
            ["Nay"] = counts.Nays ?? 0,
            ["Present"] = counts.Present ?? 0,
            ["Not Voting"] = counts.Absent ?? 0
        };
        var actual = members.GroupBy(member => member.VoteCast).ToDictionary(group => group.Key, group => group.Count());
        var matches = actual.Keys.All(expected.ContainsKey) && expected.All(item => actual.GetValueOrDefault(item.Key) == item.Value);
        if (!matches) throw new InvalidDataException("Senate vote counts differ from its member list.");
    }

    private static SenateVoteMember[] ReadMembers(XElement? members)
    {
        var items = members?.Elements("member").Select(ReadMember).ToArray() ?? [];
        if (items.Length == 0) throw new InvalidDataException("Senate vote must list members.");
        if (items.Select(member => member.LisId).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new InvalidDataException("Senate vote lists a member twice.");
        return items;
    }

    private static SenateVoteMember ReadMember(XElement member) =>
        new(SenateXml.Required(member, "lis_member_id", Name), SenateXml.Optional(member, "first_name", Name),
            SenateXml.Required(member, "last_name", Name), SenateXml.Required(member, "party", Name),
            SenateXml.Required(member, "state", Name), SenateXml.Required(member, "vote_cast", Name));
}
