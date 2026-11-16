namespace CivicLens.Publication.Contracts;

/// <summary>
/// The cast that most members of one party made on one roll call. Only Yea and Nay count. Present and Not Voting stay out of the majority.
/// Equal Yea and Nay counts give no majority.
/// </summary>
public sealed record PublishedPartyMajority
{
    public const string Yea = "Yea";
    public const string Nay = "Nay";
    public const string Democrat = "Democrat";
    public const string Republican = "Republican";
    public const string UnknownParty = "Unknown";

    public required string Party { get; init; }
    public required int YeaCount { get; init; }
    public required int NayCount { get; init; }
    public string? Majority { get; init; }

    public static PublishedPartyMajority Of(string party, int yeaCount, int nayCount) =>
        new() { Party = party, YeaCount = yeaCount, NayCount = nayCount, Majority = MajorityOf(yeaCount, nayCount) };

    public void Validate()
    {
        if (!PublishedEvidenceRules.IsBoundedText(Party) || YeaCount < 0 || NayCount < 0 || Majority != MajorityOf(YeaCount, NayCount))
            throw new InvalidDataException("Published party majority is invalid.");
    }

    private static string? MajorityOf(int yeaCount, int nayCount) => yeaCount == nayCount ? null : yeaCount > nayCount ? Yea : Nay;
}
