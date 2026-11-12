using CivicLens.PublicApi.Issues;
namespace CivicLens.PublicApi.ZipDistricts;

using CivicLens.PublicApi.Votes;

/// <summary>The outcome of a lookup. Exactly one of the answer or the reason is set.</summary>
public sealed record ZipLookup(ZipDistrictAnswer? Answer, ZipLookupMiss? Miss)
{
    /// <summary>The shared votes for the current district members, when the member view requested them.</summary>
    public SharedMemberVoteAnswer? SharedVotes { get; init; }
    /// <summary>The number of votes first published since the requested release that involve a current ZIP member.</summary>
    public long? NewVoteCount { get; init; }
    /// <summary>The issue filters published in the same release snapshot as the member view.</summary>
    public IssueOption[] IssueOptions { get; init; } = [];
    public static ZipLookup NoRelease { get; } = new(null, ZipLookupMiss.NoRelease);
    public static ZipLookup NoNames { get; } = new(null, ZipLookupMiss.NoNames);
    public static ZipLookup NotFound { get; } = new(null, ZipLookupMiss.NotFound);

    public static ZipLookup Of(ZipDistrictAnswer answer) => new(answer, null);
}

public enum ZipLookupMiss { NoRelease, NoNames, NotFound }
