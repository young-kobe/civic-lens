using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// One page of an official. The current page holds the records that name the official and the votes of the current Congress.
/// The current page of an official with a Congress also holds the issue counts and the linked pairs of that Congress.
/// Every page holds the release date, which decides whether a term has started or ended.
/// A past page holds the votes of one earlier Congress. Every page lists all Congresses of the official, newest first, for its tabs.
/// An official who left the configuration is not tracked.
/// </summary>
public sealed record ReleaseOfficialPage(PublishedOfficialProfile Profile, bool IsTracked, int? Congress, bool IsCurrent,
    ImmutableArray<int> Congresses, ImmutableArray<PublishedRecordEntry> Records, ImmutableArray<PublishedOfficialVote> Votes,
    DateTimeOffset PublishedAtUtc, TalkAndActionProfile? TalkAndAction = null, ImmutableArray<LinkedPair> Pairs = default)
{
    public string PagePath => IsCurrent
        ? PublicationProtocol.OfficialPagePath(Profile.Id)
        : PublicationProtocol.OfficialCongressPagePath(Profile.Id, Congress!.Value);

    public string RootPath => IsCurrent ? "../" : "../../";
}
