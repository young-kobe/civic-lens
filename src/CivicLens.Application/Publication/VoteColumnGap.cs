namespace CivicLens.Application.Publication;

/// <summary>
/// A published vote revision with a member list whose stored columns are not written yet. The revision sits in the record states of releases
/// <see cref="FirstRelease"/> to <see cref="LastRelease"/>, so only the folders of those releases can hold its record file. The release range is open
/// when <see cref="LastRelease"/> is null. A gap list is in order of member list, record, and revision number.
/// </summary>
public sealed record VoteColumnGap(string RecordId, int RevisionNumber, string ListId, int FirstRelease, int? LastRelease)
{
    public bool IsOpen => LastRelease is null;

    public bool Covers(int releaseNumber) => FirstRelease <= releaseNumber && (LastRelease is not { } last || releaseNumber <= last);
}
