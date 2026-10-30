using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record StatementAllowedSelections(ImmutableHashSet<string> OfficialIds, ImmutableHashSet<string> IssueIds,
    ImmutableHashSet<string> PolicyAreaIds)
{
    public void RequireAllowed(StatementDraftRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.SpeakerOfficialIds.Any(id => !OfficialIds.Contains(id)) || revision.IssueIds.Any(id => !IssueIds.Contains(id)) ||
            revision.PolicyAreaIds.Any(id => !PolicyAreaIds.Contains(id)))
            throw new ArgumentException("The saved revision includes selections that are no longer configured.");
    }

    public void RequireApprovable(StatementDraftRevision revision, DocumentExtraction extraction, bool dateReadByCode = false)
    {
        revision.ValidateForApproval();
        if (!dateReadByCode) revision.RequireConfirmedDate();
        RequireAllowed(revision);
        revision.ValidateAgainst(extraction);
    }
}
