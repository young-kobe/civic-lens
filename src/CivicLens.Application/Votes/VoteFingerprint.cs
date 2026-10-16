using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public static class VoteFingerprint
{
    public const string Version = "vote-fingerprint-v1";

    public static string Compute(ImmutableArray<VoteFact> facts, ImmutableArray<MemberVote> members)
    {
        var lines = facts.Select(fact => fact.Text)
            .Concat(members.Select(member => string.Join('|', member.BioguideId, member.PersonId, member.Cast)).Order(StringComparer.Ordinal))
            .Prepend(Version);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }
}
