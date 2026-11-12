namespace CivicLens.Application.Publication;

/// <summary>A published vote revision with a member list whose stored columns are not written yet. A gap list is in order of member list, record, and revision number.</summary>
public sealed record VoteColumnGap(string RecordId, int RevisionNumber, string ListId, KeptReleaseRange Releases);
