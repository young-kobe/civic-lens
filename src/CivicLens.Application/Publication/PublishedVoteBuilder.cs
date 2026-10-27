using System.Collections.Immutable;
using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedVoteBuilder(PublishedRecordRules rules)
{
    public PublishedVote Build(VoteDraftRevision revision, ImmutableArray<ReviewDecision> decisions,
        IReadOnlyDictionary<string, DocumentExtraction> extractions, DateTimeOffset? firstPublishedAtUtc, PublishedCorrection[] corrections, DateTimeOffset now, VoteParties parties)
    {
        var tally = VoteTally.Read(revision.RecordId, revision.Chamber, revision.Facts, extractions[revision.Members[0].Citation.ExtractionId].Text,
            revision.VotedOn, parties);
        var result = VoteFacts.Find(revision.Facts, VoteFactPrefixes.Result)
            ?? throw new ArgumentException($"Vote {revision.RecordId} has no result line.");

        PublishedCitation Cite(SourceSpan span) => PublishedRecordRules.Cite(span, extractions[span.ExtractionId]);
        PublishedMemberVote Member(MemberVote member)
        {
            var citation = Cite(member.Citation);
            return new()
            {
                Official = rules.MapOfficial(revision.RecordId, member.PersonId),
                Party = tally.PartyOf(VoteMemberLine.Parse(citation.Quote).SourceId),
                Cast = member.Cast,
                Citation = citation
            };
        }

        var record = new PublishedVote
        {
            RecordId = revision.RecordId,
            RevisionNumber = revision.RevisionNumber,
            Headline = revision.Headline,
            Summary = revision.Summary,
            TextOrigin = PublishedRecordRules.TextOriginOf(revision.TextOrigin),
            Chamber = ChamberOf(revision.Chamber),
            Congress = revision.Congress,
            Session = revision.SessionNumber,
            RollCallNumber = revision.RollCallNumber,
            Kind = PublishedVoteKinds.Of(revision.Kind),
            KindCitation = revision.KindCitation is { } kind ? Cite(kind) : null,
            VotedOn = revision.VotedOn,
            VotedOnCitation = Cite(revision.VotedOnCitation),
            Result = result.Text[VoteFactPrefixes.Result.Length..],
            Totals = tally.Totals,
            PartyMajorities = tally.PartyMajorities,
            Facts = [.. revision.Facts.Select(fact => new PublishedVoteFact { Text = fact.Text, Citation = Cite(fact.Citation) })],
            Members = [.. revision.Members.Select(Member)],
            BillType = revision.BillType,
            BillNumber = revision.BillNumber,
            TrackedIssueIds = [.. revision.TrackedIssueIds],
            Sources = PublishedRecordRules.Sources(revision.Citations, extractions),
            ApprovedAtUtc = PublishedRecordRules.GetApprovalTime(revision.RevisionNumber, decisions),
            FirstPublishedAtUtc = firstPublishedAtUtc ?? now,
            Corrections = corrections
        };
        record.Validate();
        return record;
    }

    private static PublishedChamber ChamberOf(Chamber chamber) => chamber switch
    {
        Chamber.House => PublishedChamber.House,
        Chamber.Senate => PublishedChamber.Senate,
        _ => throw new ArgumentOutOfRangeException(nameof(chamber), chamber, "Chamber has no published form.")
    };
}
