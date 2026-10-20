using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>One line of the officials page. The count holds the records that name the official, and no votes.</summary>
public sealed record OfficialListing(PublishedOfficialProfile Profile, int RecordCount)
{
    public string PagePath => PublicationProtocol.OfficialPagePath(Profile.Id);
}
