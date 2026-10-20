namespace CivicLens.Application.Publication;

/// <summary>The Congresses that an official has in the previous release: those with a vote file, and those with a past page.</summary>
internal sealed record OfficialFiles(HashSet<int> VoteCongresses, HashSet<int> PageCongresses)
{
    public static OfficialFiles None => new([], []);
}
