using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Relationships;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipTarget(RelationshipRecordKind Kind, string RecordId, int RevisionNumber, string Headline,
    ImmutableArray<RelationshipFact> Facts)
{
    public RelationshipCitation Citation => new(Kind, RecordId, RevisionNumber);

    public static RelationshipTarget Of(ActionDraftRevision action) => new(RelationshipRecordKind.Action, action.RecordId, action.RevisionNumber, action.Headline,
        Numbered([$"Bill: {action.BillType} {action.BillNumber}, Congress {action.Congress}", $"Title: {action.BillTitle}",
            $"Headline: {action.Headline}", $"Summary: {action.Summary}"]));

    public static RelationshipTarget Of(VoteDraftRevision vote) => new(RelationshipRecordKind.Vote, vote.RecordId, vote.RevisionNumber, vote.Headline,
        Numbered(vote.Facts.Select(fact => fact.Text)));

    private static ImmutableArray<RelationshipFact> Numbered(IEnumerable<string> texts) =>
        [.. texts.Select((text, index) => new RelationshipFact($"F{index + 1}", text))];
}
