using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// One page of an official. The current page holds the records that name the official and the votes of the current Congress.
/// A past page holds the votes of one earlier Congress. An official who left the configuration is not tracked.
/// </summary>
public sealed record ReleaseOfficialPage(PublishedOfficialProfile Profile, bool IsTracked, int? Congress, bool IsCurrent,
    ImmutableArray<int> PastCongresses, ImmutableArray<PublishedRecordEntry> Records, ImmutableArray<PublishedOfficialVote> Votes)
{
    public string PagePath => IsCurrent
        ? PublicationProtocol.OfficialPagePath(Profile.Id)
        : PublicationProtocol.OfficialCongressPagePath(Profile.Id, Congress!.Value);

    public string RootPath => IsCurrent ? "../" : "../../";
}
