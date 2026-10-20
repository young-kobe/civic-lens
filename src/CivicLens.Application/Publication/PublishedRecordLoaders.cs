using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Briefs;
using CivicLens.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Application.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class PublishedRecordLoaders(IDocumentChangeReviewStore documentChanges, IStatementReviewStore statements,
    IActionReviewStore actions, IVoteReviewStore votes, IRelationshipReviewStore relationships, IBriefReviewStore briefs)
{
    internal ImmutableArray<PublishedRecordLoader> All { get; } =
        [new DocumentChangeRecordLoader(documentChanges), new StatementRecordLoader(statements), new ActionRecordLoader(actions),
         new VoteRecordLoader(votes), new RelationshipRecordLoader(relationships), new BriefRecordLoader(briefs)];

    internal RelationshipRecordLoader Relationships => All.OfType<RelationshipRecordLoader>().Single();

    internal VoteRecordLoader Votes => All.OfType<VoteRecordLoader>().Single();

    internal ICarriedPageLoader Carried(PublishedRecordKind kind) =>
        For(kind) as ICarriedPageLoader ?? throw new ArgumentException($"A {kind} page does not list related records.", nameof(kind));

    internal PublishedRecordLoader For(PublishedRecordKind kind) =>
        All.FirstOrDefault(loader => loader.Kind == kind) ?? throw new ArgumentException("Record kind is unknown.", nameof(kind));
}
