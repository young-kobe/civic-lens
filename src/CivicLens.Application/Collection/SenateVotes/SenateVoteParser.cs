using System.Xml.Linq;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Application.Collection.SenateVotes;

public static class SenateVoteParser
{
    public const string Version = "senate-vote-v1";

    private const string Name = "Senate vote";

    public static SenateVoteDetail Parse(ReadOnlySpan<byte> xml, SenateVoteRoll expected)
    {
        var root = SenateXml.Load(xml, Name, "roll_call_vote");
        var session = SenateXml.ReadSession(root, Name);
        if (SenateXml.ReadRoll(session, SenateXml.Required(root, "vote_number", Name), Name) != expected)
            throw new InvalidDataException("Senate vote is for another roll call than requested.");
        var document = ReadDocument(root.Element("document"));
        var billType = BillTypeOf(document);
        var vote = new SenateVote(expected, SenateXml.ReadDateTime(SenateXml.Required(root, "modify_date", Name), Name),
            billType, billType is null ? null : document!.Number);
        return new SenateVoteDetail(vote, SenateXml.ReadDateTime(SenateXml.Required(root, "vote_date", Name), Name),
            SenateXml.Required(root, "question", Name), SenateXml.Required(root, "vote_question_text", Name),
            SenateXml.Required(root, "vote_title", Name), SenateXml.Required(root, "vote_result", Name),
            SenateXml.Optional(root, "vote_result_text", Name), SenateXml.Optional(root, "vote_document_text", Name),
            SenateXml.Optional(root, "majority_requirement", Name), document, ReadCounts(root),
            ReadMembers(root.Element("members")));
    }

    private static SenateVoteDocument? ReadDocument(XElement? element)
    {
        if (element is null || SenateXml.Optional(element, "document_type", Name) is not { } type) return null;
        var number = ApiPageQuery.TryReadNumber(SenateXml.Required(element, "document_number", Name))
            ?? throw new InvalidDataException("Senate vote document number is invalid.");
        return new SenateVoteDocument(type, number, SenateXml.Required(element, "document_name", Name),
            SenateXml.Optional(element, "document_title", Name));
    }

    private static string? BillTypeOf(SenateVoteDocument? document)
    {
        if (document is null || CongressBillListItem.NormalizeCitationType(document.Type) is not { } type) return null;
        return document.Number >= 1 ? type : throw new InvalidDataException("Senate vote bill number must be 1 or more.");
    }

    private static SenateVoteCounts ReadCounts(XElement root)
    {
        var count = root.Element("count") ?? throw new InvalidDataException("Senate vote must contain count.");
        return new SenateVoteCounts(SenateXml.OptionalCount(count, "yeas", Name), SenateXml.OptionalCount(count, "nays", Name),
            SenateXml.OptionalCount(count, "present", Name), SenateXml.OptionalCount(count, "absent", Name));
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
