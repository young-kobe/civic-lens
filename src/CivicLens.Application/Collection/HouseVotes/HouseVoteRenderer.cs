using System.Globalization;
using static CivicLens.Application.Collection.RenderedText;

namespace CivicLens.Application.Collection.HouseVotes;

public static class HouseVoteRenderer
{
    private const string StartedFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    public const string NormalizationVersion = "house-vote-render-v2";

    public static string RenderDetail(HouseVoteDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var lines = new List<string> { IdentityLine("House vote", detail.Vote), UpdatedLine(detail.Vote) };
        AddLine(lines, "Started", detail.StartDate is { } start ? start.ToString(StartedFormat, CultureInfo.InvariantCulture) : null);
        AddLine(lines, "Question", detail.Question);
        AddLine(lines, "Vote type", detail.VoteType);
        AddLine(lines, "Result", detail.Result);
        AddLine(lines, "Legislation", LegislationOf(detail));
        lines.AddRange(detail.PartyTotals.Select(PartyTotalLine).Order(StringComparer.Ordinal));
        lines.AddRange(detail.CandidateTotals.Select(CandidateTotalLine).Order(StringComparer.Ordinal));
        AddLine(lines, "Source data", detail.SourceDataUrl);
        return string.Join('\n', lines);
    }

    public static string RenderMembers(HouseVoteMemberVotes votes)
    {
        ArgumentNullException.ThrowIfNull(votes);
        var members = votes.Members.Select(MemberLine).Order(StringComparer.Ordinal);
        return string.Join('\n', members.Prepend(UpdatedLine(votes.Vote)).Prepend(IdentityLine("House vote members", votes.Vote)));
    }

    private static string IdentityLine(string label, HouseVote vote) =>
        Line(label, string.Join(' ', Count(vote.Roll.Session.Congress), Count(vote.Roll.Session.Number), Count(vote.Roll.Number)));

    private static string UpdatedLine(HouseVote vote) => Line("Updated", ApiJobKeys.ExactTime(vote.UpdateDate));

    private static string? LegislationOf(HouseVoteDetail detail) =>
        string.IsNullOrWhiteSpace(detail.LegislationType) ? null : $"{detail.LegislationType} {detail.LegislationNumber}";

    private static string PartyTotalLine(HouseVotePartyTotal total)
    {
        var counts = new (string Label, int? Value)[]
        {
            ("Yea", total.Yea), ("Nay", total.Nay), ("Present", total.Present), ("Not voting", total.NotVoting)
        };
        var present = counts.Where(count => count.Value is not null).Select(count => $"{count.Label} {Count(count.Value!.Value)}");
        return Line($"Party total {total.Party}", string.Join(", ", present));
    }

    private static string CandidateTotalLine(HouseVoteCandidateTotal total) =>
        Line($"Candidate total {SingleLine(total.Candidate)}", Count(total.Total));

    public static string MemberLine(HouseVoteMember member)
    {
        var name = SingleLine($"{member.FirstName} {member.LastName}");
        var fields = new[] { member.BioguideId, name, member.Party ?? "", member.State ?? "", member.VoteCast };
        return Line("Member", string.Join(", ", fields.Select(SingleLine)));
    }
}
