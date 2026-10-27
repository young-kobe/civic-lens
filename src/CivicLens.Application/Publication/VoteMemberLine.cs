namespace CivicLens.Application.Publication;

internal enum VoteCastKind { Yea, Nay, Present, NotVoting }

/// <summary>One saved "Member: id, name, party, state, cast" line. The name may hold commas, so the party and the cast count from the end.</summary>
internal sealed record VoteMemberLine(string SourceId, string SourceParty, VoteCastKind Cast)
{
    public const string Prefix = "Member: ";

    private const int MinimumFields = 5;

    public static VoteMemberLine Parse(string line)
    {
        var fields = line.StartsWith(Prefix, StringComparison.Ordinal) ? line[Prefix.Length..].Split(", ") : [];
        if (fields.Length < MinimumFields)
            throw new ArgumentException($"A member line is not 'id, name, party, state, cast': {line}");
        return new(fields[0], fields[^3], CastOf(fields[^1]));
    }

    private static VoteCastKind CastOf(string cast) => cast switch
    {
        "Yea" or "Aye" => VoteCastKind.Yea,
        "Nay" or "No" => VoteCastKind.Nay,
        "Present" => VoteCastKind.Present,
        "Not Voting" => VoteCastKind.NotVoting,
        _ => throw new ArgumentException($"The cast '{cast}' is not Yea, Nay, Present, or Not Voting.")
    };
}
