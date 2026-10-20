using CivicLens.Core.Actions;
using CivicLens.Core.Relationships;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipDraftEvidence(string PairId, RelationshipRecordKind TargetKind, ApprovedStatement? Statement,
    ActionDraftRevision? Action, VoteDraftRevision? Vote, string? SettledFingerprint, RelationshipRevision? Current,
    bool SettledAsNone)
{
    public bool SidesAreApproved => Statement is not null && (TargetKind == RelationshipRecordKind.Action ? Action is not null : Vote is not null);

    public RelationshipPair? FindPair() => TargetKind == RelationshipRecordKind.Action
        ? RelationshipPairFinder.Match(Statement!, Action!)
        : RelationshipPairFinder.Match(Statement!, Vote!);
}
