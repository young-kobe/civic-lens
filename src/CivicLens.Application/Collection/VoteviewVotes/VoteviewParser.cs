using System.Globalization;
using System.Text.RegularExpressions;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.VoteviewVotes;

public static partial class VoteviewParser
{
    public const string ExtractionVersion = "voteview-senate-vote-v1";

    private const string RollCallsName = "Voteview roll call file";
    private const string VotesName = "Voteview vote file";
    private const string MembersName = "Voteview member file";
    private const string PresidentChamber = "President";
    private const string DayFormat = "yyyy-MM-dd";

    public static IReadOnlyList<VoteviewRollCall> ParseRollCalls(ReadOnlySpan<byte> body, int congress)
    {
        var csv = VoteviewCsv.Read(body, RollCallsName, "congress", "chamber", "rollnumber", "date", "session", "clerk_rollnumber",
            "majority_requirement", "yea_count", "nay_count", "bill_number", "vote_result", "vote_desc", "vote_question");
        var rollCalls = csv.Rows.Select(row => ReadRollCall(csv, row, congress)).ToArray();
        if (rollCalls.Select(rollCall => rollCall.Part).Distinct().Count() != rollCalls.Length)
            throw new InvalidDataException($"{RollCallsName} lists a roll call twice.");
        return rollCalls;
    }

    public static IReadOnlyList<VoteviewCast> ParseCasts(ReadOnlySpan<byte> body, int congress)
    {
        var csv = VoteviewCsv.Read(body, VotesName, "congress", "chamber", "rollnumber", "icpsr", "cast_code");
        return [.. csv.Rows.Select(row =>
        {
            RequireSenateOf(csv, row, congress, VotesName);
            return new VoteviewCast(csv.Number(row, "rollnumber"), csv.Number(row, "icpsr"), CastOf(csv.Required(row, "cast_code")));
        })];
    }

    public static VoteviewMemberFile ParseMembers(ReadOnlySpan<byte> body, int congress)
    {
        var csv = VoteviewCsv.Read(body, MembersName, "congress", "chamber", "icpsr", "state_abbrev", "party_code", "bioname", "bioguide_id");
        var senators = new Dictionary<int, VoteviewMember>();
        var presidents = new HashSet<int>();
        foreach (var row in csv.Rows)
        {
            if (csv.Required(row, "chamber") == PresidentChamber)
            {
                presidents.Add(csv.Number(row, "icpsr"));
                continue;
            }
            RequireSenateOf(csv, row, congress, MembersName);
            var member = new VoteviewMember(csv.Number(row, "icpsr"), csv.Required(row, "bioguide_id"), csv.Required(row, "bioname"),
                PartyOf(csv.Number(row, "party_code")), csv.Required(row, "state_abbrev"));
            if (!senators.TryAdd(member.Icpsr, member)) throw new InvalidDataException($"{MembersName} lists a member twice.");
        }
        return new VoteviewMemberFile(senators, presidents);
    }

    private static VoteviewRollCall ReadRollCall(VoteviewCsv csv, string[] row, int congress)
    {
        RequireSenateOf(csv, row, congress, RollCallsName);
        var session = VoteSession.TryCreate(congress, csv.Number(row, "session"))
            ?? throw new InvalidDataException($"{RollCallsName} has an invalid session.");
        var clerkNumber = csv.Number(row, "clerk_rollnumber");
        if (clerkNumber < 1) throw new InvalidDataException($"{RollCallsName} has an invalid clerk roll number.");
        var (billType, billNumber) = ReadBill(csv.Optional(row, "bill_number"));
        return new VoteviewRollCall(csv.Number(row, "rollnumber"), new SenateVoteRoll(session, clerkNumber), ReadDay(csv.Required(row, "date")),
            csv.Required(row, "vote_question"), csv.Optional(row, "vote_desc"), csv.Required(row, "vote_result"),
            csv.Optional(row, "majority_requirement"), billType, billNumber, csv.Number(row, "yea_count"), csv.Number(row, "nay_count"));
    }

    private static void RequireSenateOf(VoteviewCsv csv, string[] row, int congress, string name)
    {
        if (csv.Number(row, "congress") != congress || csv.Required(row, "chamber") != "Senate")
            throw new InvalidDataException($"{name} has a row of another Congress or chamber.");
    }

    private static DateOnly ReadDay(string text) =>
        DateOnly.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new InvalidDataException($"{RollCallsName} has an invalid date.");

    private static (string? Type, int? Number) ReadBill(string? text)
    {
        if (text is null || BillPattern().Match(text) is not { Success: true } match) return (null, null);
        var type = CongressBillListItem.NormalizeType(match.Groups[1].Value);
        var number = ApiPageQuery.TryReadNumber(match.Groups[2].Value);
        return type is not null && number is >= 1 ? (type, number) : (null, null);
    }

    private static string CastOf(string code) => code switch
    {
        "1" => VoteviewCast.Yea,
        "6" => VoteviewCast.Nay,
        "7" => VoteviewCast.Present,
        "9" => VoteviewCast.NotVoting,
        _ => throw new InvalidDataException($"{VotesName} has an unknown cast code.")
    };

    private static string PartyOf(int code) => code switch
    {
        100 => "D",
        200 => "R",
        328 => "I",
        _ => throw new InvalidDataException($"{MembersName} has an unknown party code.")
    };

    [GeneratedRegex(@"^([A-Z]+)(\d{1,9})$")]
    private static partial Regex BillPattern();
}
