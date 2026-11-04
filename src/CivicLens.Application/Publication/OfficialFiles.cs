namespace CivicLens.Application.Publication;

/// <summary>The files that an official has in the previous release: the Congresses with a vote file, the Congresses with a past page, and the record page.</summary>
internal sealed record OfficialFiles(HashSet<int> VoteCongresses, HashSet<int> PageCongresses, bool HasRecordPage)
{
    public static OfficialFiles None => new([], [], false);
}
