using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Briefs;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Application.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class PublishedRecordLoaders(IDocumentChangeReviewStore documentChanges, IStatementReviewStore statements,
    IActionReviewStore actions, IVoteReviewStore votes, IBriefReviewStore briefs)
{
    internal ImmutableArray<PublishedRecordLoader> All { get; } =
        [new DocumentChangeRecordLoader(documentChanges), new StatementRecordLoader(statements), new ActionRecordLoader(actions),
         new VoteRecordLoader(votes), new BriefRecordLoader(briefs)];

    internal PublishedRecordLoader For(PublishedRecordKind kind) =>
        All.FirstOrDefault(loader => loader.Kind == kind) ?? throw new ArgumentException("Record kind is unknown.", nameof(kind));
}
