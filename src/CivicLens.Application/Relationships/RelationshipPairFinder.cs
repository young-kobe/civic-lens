using System.Collections.Immutable;
using System.Globalization;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Relationships;

public static class RelationshipPairFinder
{
    public static RelationshipPairs Find(IReadOnlyCollection<ApprovedStatement> statements, IReadOnlyCollection<ActionDraftRevision> actions,
        IReadOnlyCollection<VoteDraftRevision> votes)
    {
        var titleKeys = actions.Select(action => (Action: action, Key: BillTitleMatcher.KeyOf(action.BillTitle))).ToArray();
        var found = ImmutableArray.CreateBuilder<RelationshipPair>();
        var skippedWithoutDate = 0;
        foreach (var statement in statements)
        {
            var mentions = StatementBillMentions.In(statement);
            if (mentions.Any(mention => mention.Congress is null)) skippedWithoutDate++;
            found.AddRange(titleKeys.Select(item => MatchAction(statement, mentions, item.Action, item.Key)).OfType<RelationshipPair>());
            found.AddRange(votes.Select(vote => MatchVote(statement, mentions, vote)).OfType<RelationshipPair>());
        }
        return new(found.ToImmutable(), skippedWithoutDate);
    }

    public static ImmutableArray<(int Congress, string BillType, int Number)> BillsMentionedIn(IEnumerable<ApprovedStatement> statements) =>
        [.. statements.SelectMany(statement => StatementBillMentions.In(statement)).Where(mention => mention.Congress is not null)
            .Select(mention => (mention.Congress!.Value, mention.BillType, mention.Number)).Distinct()];

    public static RelationshipPair? Match(ApprovedStatement statement, ActionDraftRevision action) =>
        MatchAction(statement, StatementBillMentions.In(statement), action, BillTitleMatcher.KeyOf(action.BillTitle));

    public static RelationshipPair? Match(ApprovedStatement statement, VoteDraftRevision vote) =>
        MatchVote(statement, StatementBillMentions.In(statement), vote);

    private static RelationshipPair? MatchAction(ApprovedStatement statement, ImmutableArray<BillMention> mentions, ActionDraftRevision action,
        string? titleKey)
    {
        var sponsorship = action.Sponsorships.Where(item => statement.Revision.SpeakerOfficialIds.Contains(item.PersonId))
            .OrderBy(item => item.Role).FirstOrDefault();
        if (sponsorship is null) return null;
        var match = MatchByNumber(mentions, action.Congress, action.BillType, action.BillNumber) ?? MatchByTitle(statement, titleKey);
        return match is null ? null
            : new(statement, RelationshipTarget.Of(action), match, sponsorship.PersonId, SponsorshipRole(sponsorship));
    }

    private static RelationshipPair? MatchVote(ApprovedStatement statement, ImmutableArray<BillMention> mentions, VoteDraftRevision vote)
    {
        var member = vote.Members.FirstOrDefault(item => statement.Revision.SpeakerOfficialIds.Contains(item.PersonId));
        if (member is null) return null;
        var match = MatchByNumber(mentions, vote.Congress, vote.BillType, vote.BillNumber);
        return match is null ? null : new(statement, RelationshipTarget.Of(vote), match, member.PersonId, $"voted {member.Cast}");
    }

    private static RelationshipMatch? MatchByNumber(ImmutableArray<BillMention> mentions, int congress, string? billType, int? billNumber) =>
        mentions.FirstOrDefault(mention => mention.IsOf(congress, billType, billNumber)) is { } mention
            ? new(RelationshipMatchBasis.BillNumber, mention.Span)
            : null;

    private static RelationshipMatch? MatchByTitle(ApprovedStatement statement, string? titleKey)
    {
        if (titleKey is null) return null;
        foreach (var passage in statement.Passages())
            if (BillTitleMatcher.Find(passage.Text, titleKey) is { } found)
                return new(RelationshipMatchBasis.BillTitle,
                    new SourceSpan(passage.Span.ExtractionId, passage.Span.Start + found.Start, found.Length));
        return null;
    }

    private static string SponsorshipRole(ActionSponsorship sponsorship)
    {
        var role = sponsorship.Role == ActionSponsorshipRole.Sponsor ? "sponsor" : sponsorship.IsOriginal ? "original cosponsor" : "cosponsor";
        return sponsorship.WithdrawnOn is { } withdrawn ? $"{role}, withdrawn on {withdrawn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : role;
    }
}
