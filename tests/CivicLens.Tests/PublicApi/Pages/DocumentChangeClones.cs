using CivicLens.Core.Review;

namespace CivicLens.Tests.PublicApi.Pages;

/// <summary>Copies the published revision of a document change under a new draft ID, so that a list test can hold many changes without a review flow for each.
/// The copy names a comparison that does not exist, so the view gives it no place count. The foreign keys are off for the copy. A null date also clears the date evidence.</summary>
internal static class DocumentChangeClones
{
    public static string SetDate(string draft, string changeDate) =>
        $"UPDATE document_change_revisions SET change_date = '{changeDate}' WHERE draft_id = '{draft}';";

    public static string Of(string sourceDraft, string newDraft, string headline, string institution, string? changeDate, bool model) => $"""
        SET session_replication_role = replica;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
          SELECT kind, '{newDraft}', first_release, revision_number, last_release FROM publication_record_states WHERE record_id = '{sourceDraft}';
        INSERT INTO document_change_revisions
          SELECT (json_populate_record(r, json_build_object(
              'draft_id', '{newDraft}', 'headline', '{headline}', 'institution', '{institution}', 'comparison_id', '{new string('f', 64)}',
              'change_date', {(changeDate is null ? "NULL" : $"'{changeDate}'")}{(changeDate is null ? ", 'change_date_evidence_extraction_id', NULL, 'change_date_evidence_start', NULL, 'change_date_evidence_length', NULL" : "")}
              {(model ? $", 'author_kind', 'Analysis', 'author_subject', '{ReviewAuthor.AnalysisSubject}'" : "")}))).*
            FROM document_change_revisions r WHERE r.draft_id = '{sourceDraft}';
        """;
}
