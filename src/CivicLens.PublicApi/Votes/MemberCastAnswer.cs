namespace CivicLens.PublicApi.Votes;

/// <summary>The recorded casts of the asked officials on the roll call votes that name one bill, in the active release, oldest first.</summary>
public sealed record MemberCastAnswer(int Release, MemberBillCast[] Casts);
