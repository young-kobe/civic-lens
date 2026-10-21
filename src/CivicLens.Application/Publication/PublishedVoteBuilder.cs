using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedVoteBuilder(PublishedRecordRules rules)
{
    public PublishedVote Build(VoteDraftRevision revision, ImmutableArray<ReviewDecision> decisions,
        IReadOnlyDictionary<string, DocumentExtraction> extractions, PublishedRecordEntry? previous, PublishedCorrection[] corrections, DateTimeOffset now)
    {
        PublishedCitation Cite(SourceSpan span) => PublishedRecordRules.Cite(span, extractions[span.ExtractionId]);

        var record = new PublishedVote
        {
            RecordId = revision.RecordId,
            RevisionNumber = revision.RevisionNumber,
            Headline = revision.Headline,
            Summary = revision.Summary,
            TextOrigin = PublishedRecordRules.TextOriginOf(revision.TextOrigin),
            Congress = revision.Congress,
            Session = revision.SessionNumber,
            RollCallNumber = revision.RollCallNumber,
            Kind = KindOf(revision.Kind),
            KindCitation = revision.KindCitation is { } kind ? Cite(kind) : null,
            VotedOn = revision.VotedOn,
            VotedOnCitation = Cite(revision.VotedOnCitation),
            Facts = [.. revision.Facts.Select(fact => new PublishedVoteFact { Text = fact.Text, Citation = Cite(fact.Citation) })],
            Members = [.. revision.Members.Select(member => new PublishedMemberVote
            {
                Official = rules.MapOfficial(revision.RecordId, member.PersonId),
                Cast = member.Cast,
                Citation = Cite(member.Citation)
            })],
            BillType = revision.BillType,
            BillNumber = revision.BillNumber,
            TrackedIssueIds = [.. revision.TrackedIssueIds],
            Sources = PublishedRecordRules.Sources(revision.Citations, extractions),
            ApprovedAtUtc = PublishedRecordRules.GetApprovalTime(revision.RevisionNumber, decisions),
            FirstPublishedAtUtc = previous?.FirstPublishedAtUtc ?? now,
            Corrections = corrections
        };
        record.Validate();
        return record;
    }

    private static PublishedVoteKind KindOf(VoteKind kind) => kind switch
    {
        VoteKind.Rule => PublishedVoteKind.Rule,
        VoteKind.PreviousQuestion => PublishedVoteKind.PreviousQuestion,
        VoteKind.MotionToRecommit => PublishedVoteKind.MotionToRecommit,
        VoteKind.Amendment => PublishedVoteKind.Amendment,
        VoteKind.Suspension => PublishedVoteKind.Suspension,
        VoteKind.Passage => PublishedVoteKind.Passage,
        VoteKind.ConferenceReport => PublishedVoteKind.ConferenceReport,
        VoteKind.VetoOverride => PublishedVoteKind.VetoOverride,
        VoteKind.Other => PublishedVoteKind.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no published form.")
    };
}
