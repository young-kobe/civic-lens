using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Core.Statements;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Relationships;

public static class RelationshipPairRule
{
    public static bool IsSameBill(int? mentionedCongress, string mentionedType, int mentionedNumber, int congress, string? billType,
        int? billNumber) =>
        mentionedCongress == congress && string.Equals(mentionedType, billType, StringComparison.Ordinal) && mentionedNumber == billNumber;

    public static ImmutableArray<(string BillType, int Number)> CitedBills(string text) =>
        [.. BillNumberFinder.FindWithSpans(text).Select(match => (BillTypeOf(match), match.Number)).Distinct()];

    internal static string BillTypeOf(BillNumberMatch match) => CongressBillListItem.NormalizeCitationType(match.Type)!;

    internal static ActionSponsorship? SponsorOf(ImmutableArray<string> speakerOfficialIds, ActionDraftRevision action) =>
        action.Sponsorships.Where(item => speakerOfficialIds.Contains(item.PersonId)).OrderBy(item => item.Role).FirstOrDefault();

    internal static MemberVote? MemberOf(ImmutableArray<string> speakerOfficialIds, VoteDraftRevision vote) =>
        vote.Members.FirstOrDefault(item => speakerOfficialIds.Contains(item.PersonId));
}
