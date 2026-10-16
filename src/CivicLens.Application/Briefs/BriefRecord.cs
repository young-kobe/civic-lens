using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Briefs;

public sealed record BriefRecord(BriefCitationKind Kind, string RecordId, int RevisionNumber, string Headline, string Summary,
    DateOnly? Date, ImmutableArray<string> OfficialIds, string? Bill, int SponsorshipEntries)
{
    public static BriefRecord From(DocumentChangeDraftRevision revision) => new(BriefCitationKind.DocumentChange, revision.DraftId,
        revision.RevisionNumber, revision.Headline, revision.Summary, revision.ChangeDate, revision.OfficialIds, null, 0);

    public static BriefRecord From(StatementDraftRevision revision) => new(BriefCitationKind.Statement, revision.CandidateId,
        revision.RevisionNumber, revision.Headline, revision.Summary, revision.StatementDate, revision.SpeakerOfficialIds, null, 0);

    public static BriefRecord From(ActionDraftRevision revision) => new(BriefCitationKind.Action, revision.RecordId,
        revision.RevisionNumber, revision.Headline, revision.Summary, null,
        [.. revision.Sponsorships.Select(item => item.PersonId).Distinct(StringComparer.Ordinal)],
        $"{revision.Congress} {revision.BillType} {revision.BillNumber}", revision.Sponsorships.Length);
}
