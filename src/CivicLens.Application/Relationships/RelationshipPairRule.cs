using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Relationships;

public static class RelationshipPairRule
{
    public static bool IsSameBill(int? mentionedCongress, string mentionedType, int mentionedNumber, int congress, string? billType,
        int? billNumber) =>
        mentionedCongress == congress && string.Equals(mentionedType, billType, StringComparison.Ordinal) && mentionedNumber == billNumber;

    internal static ActionSponsorship? SponsorOf(ImmutableArray<string> speakerOfficialIds, ActionDraftRevision action) =>
        action.Sponsorships.Where(item => speakerOfficialIds.Contains(item.PersonId)).OrderBy(item => item.Role).FirstOrDefault();

    internal static MemberVote? MemberOf(ImmutableArray<string> speakerOfficialIds, VoteDraftRevision vote) =>
        vote.Members.FirstOrDefault(item => speakerOfficialIds.Contains(item.PersonId));
}
