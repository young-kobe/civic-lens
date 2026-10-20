using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>One official with a page: the manifest entries that name the official, and the votes of the official.</summary>
public sealed record ReleaseOfficial(PublishedOfficialProfile Profile, ImmutableArray<PublishedRecordEntry> Records,
    ImmutableArray<PublishedOfficialVote> Votes)
{
    public string PagePath => PublicationProtocol.OfficialPagePath(Profile.Id);

    public int RecordCount => Records.Length + Votes.Length;
}
