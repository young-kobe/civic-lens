using System.Globalization;
using CivicLens.Application.Votes;
using static CivicLens.Application.Collection.RenderedText;

namespace CivicLens.Application.Collection.SenateVotes;

public static class SenateVoteRenderer
{
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    public static string Render(SenateVoteDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var roll = detail.Vote.Roll;
        var lines = new List<string>
        {
            Line("Senate vote", string.Join(' ', Count(roll.Session.Congress), Count(roll.Session.Number), Count(roll.Number))),
            Line("Updated", ApiJobKeys.ExactTime(detail.Vote.ModifyDate)),
            Line("Started", detail.VoteDate.ToString(TimeFormat, CultureInfo.InvariantCulture)),
            Line("Question", detail.Question),
            Line("Question text", detail.QuestionText),
            Line("Vote title", detail.Title),
            Line("Result", detail.Result)
        };
        AddLine(lines, "Result text", detail.ResultText);
        AddLine(lines, "Majority required", detail.MajorityRequirement);
        AddLine(lines, "Document", detail.DocumentName);
        AddLine(lines, "Document text", detail.DocumentText);
        lines.Add(Line("Count", CountsOf(detail.Counts)));
        lines.AddRange(detail.Members.Select(MemberLine).Order(StringComparer.Ordinal));
        return string.Join('\n', lines);
    }

    private static string CountsOf(SenateVoteCounts counts) =>
        $"{VoteCountLabels.Yea} {Count(counts.Yeas ?? 0)}, {VoteCountLabels.Nay} {Count(counts.Nays ?? 0)}, "
        + $"{VoteCountLabels.Present} {Count(counts.Present ?? 0)}, {VoteCountLabels.NotVoting} {Count(counts.Absent ?? 0)}";

    public static string MemberLine(SenateVoteMember member)
    {
        var name = SingleLine($"{member.FirstName} {member.LastName}");
        var fields = new[] { member.LisId, name, member.Party, member.State, member.VoteCast };
        return Line("Member", string.Join(", ", fields.Select(SingleLine)));
    }
}
