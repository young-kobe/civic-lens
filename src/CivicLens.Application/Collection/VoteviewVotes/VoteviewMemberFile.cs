namespace CivicLens.Application.Collection.VoteviewVotes;

/// <summary>The senators of the member file and the icpsr numbers of its President rows, whose casts are not Senate votes.</summary>
public sealed record VoteviewMemberFile(IReadOnlyDictionary<int, VoteviewMember> Senators, IReadOnlySet<int> PresidentIcpsrs);
