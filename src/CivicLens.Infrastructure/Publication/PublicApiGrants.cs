using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class PublicApiGrants
{
    private const string Sql = """
        DO $$
        DECLARE
          lock_function regprocedure;
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_lens_public') THEN
            CREATE ROLE civic_lens_public NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
          END IF;
          EXECUTE format('GRANT CONNECT ON DATABASE %I TO civic_lens_public', current_database());
          EXECUTE format('REVOKE TEMPORARY ON DATABASE %I FROM PUBLIC', current_database());
          FOR lock_function IN SELECT p.oid::regprocedure FROM pg_proc p
                                WHERE p.pronamespace = 'pg_catalog'::regnamespace AND p.proname LIKE 'pg\_%advisory\_%'
          LOOP
            EXECUTE format('REVOKE EXECUTE ON FUNCTION %s FROM PUBLIC', lock_function);
          END LOOP;
        END $$;

        ALTER ROLE civic_lens_public CONNECTION LIMIT 5;
        ALTER ROLE civic_lens_public SET default_transaction_read_only = on;
        ALTER ROLE civic_lens_public SET statement_timeout = '2s';
        ALTER ROLE civic_lens_public SET idle_in_transaction_session_timeout = '10s';
        -- Keep this above PublicApiOptions.PublicViewsIdleLifetime plus the 10 second pruning interval of the pool of the API.
        ALTER ROLE civic_lens_public SET idle_session_timeout = '60s';

        GRANT SELECT (release_number, published_at_utc_ticks, is_active) ON publication_releases TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, first_release, last_release, withdrawn_revision_number, withdrawn_at_utc_ticks, note) ON publication_withdrawals TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, first_release, "position", replaced_revision_number, published_at_utc_ticks, note, facts_unchanged) ON publication_withdrawal_corrections TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, first_published_at_utc_ticks) ON published_records TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, first_release, revision_number, last_release) ON publication_record_states TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, chamber, congress, session_number, roll_call_number, voted_on, kind,
            bill_type, bill_number, headline, summary, text_origin, member_list_id, question, result, alignment, democrat_majority, republican_majority, columns_written, party_tally_count,
            vote_source, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length,
            kind_citation_extraction_id, kind_citation_start, kind_citation_length) ON vote_revisions TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, party, yea, nay, present, not_voting, other)
            ON vote_revision_party_tallies TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, revision_number, event_date, link_type, statement_kind, statement_record_id, target_kind, target_record_id, bill_line, document_type, approved_by)
            ON published_entries TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, reason) ON relationship_revisions TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, revision_number, official_id) ON published_entry_officials TO civic_lens_public_owner;
        GRANT SELECT (list_id, "position", person_id, vote_cast, party, citation_extraction_id, citation_start, citation_length)
            ON vote_member_list_entries TO civic_lens_public_owner;
        GRANT SELECT (list_id, yea_count, nay_count, present_count, not_voting_count, other_count)
            ON vote_member_lists TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, issue_id, is_tracked) ON vote_revision_issues TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, congress, bill_type, bill_number, bill_title, headline, summary, text_origin,
            policy_area, policy_area_id, event_on, cosponsor_count, text_package_id, why_it_matters) ON action_revisions TO civic_lens_public_owner;
        GRANT SELECT (congress, bill_type, bill_number, package_id, version_type, version_date, chars_read, chars_total)
            ON bill_text_versions TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, "position", person_id, role, is_original, sponsored_on, withdrawn_on)
            ON action_revision_sponsorships TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, issue_id, "position", is_tracked) ON action_revision_issues TO civic_lens_public_owner;
        GRANT SELECT (crs_summary_text, summary_version) ON action_revisions TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, week_start, headline, text_origin) ON brief_revisions TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, part, position, paragraph_text) ON brief_revision_paragraphs TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, part, citation_id, position, kind, statement_candidate_id, statement_revision_number,
            action_record_id, action_revision_number, document_change_draft_id, document_change_revision_number) ON brief_revision_citations TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, part, paragraph_position, position, citation_id) ON brief_revision_paragraph_citations TO civic_lens_public_owner;
        GRANT SELECT (candidate_id, revision_number, author_kind, headline, summary, statement_date,
            statement_date_evidence_extraction_id, statement_date_evidence_start, statement_date_evidence_length) ON statement_revisions TO civic_lens_public_owner;
        GRANT SELECT (candidate_id, revision_number, official_id, "position") ON statement_revision_speakers TO civic_lens_public_owner;
        GRANT SELECT (candidate_id, revision_number, issue_id, "position") ON statement_revision_issues TO civic_lens_public_owner;
        GRANT SELECT (candidate_id, url, extraction_id, attempt_id) ON statement_candidates TO civic_lens_public_owner;
        GRANT SELECT (extraction_id, attempt_id, text) ON document_extractions TO civic_lens_public_owner;
        GRANT SELECT (attempt_id, requested_url, final_url, observed_at_utc_ticks) ON collection_attempts TO civic_lens_public_owner;
        GRANT SELECT (candidate_id, revision_number, position, quotation_extraction_id, quotation_start, quotation_length, kind)
            ON statement_revision_quotations TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, "position", fact_text, citation_extraction_id, citation_start, citation_length)
            ON vote_revision_facts TO civic_lens_public_owner;
        GRANT SELECT (release_number, record_id, latest_action_text, latest_action_date, read_at_utc_ticks) ON release_bill_statuses TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, kind, document_number, signed_on, published_on, federal_register_citation, document_url,
            person_id, president_name, title) ON executive_action_revisions TO civic_lens_public_owner;
        GRANT SELECT (draft_id, revision_number, comparison_id, headline, summary, author_kind, significance, limits, institution, change_date)
            ON document_change_revisions TO civic_lens_public_owner;
        GRANT SELECT (comparison_id, before_extraction_id, after_extraction_id, hunk_count) ON document_comparisons TO civic_lens_public_owner;
        GRANT SELECT (comparison_id, position, before_start, before_length, after_start, after_length, before_context_start, before_context_length,
            after_context_start, after_context_length, word_edit_count) ON document_comparison_hunks TO civic_lens_public_owner;
        GRANT SELECT (comparison_id, hunk_position, position, before_start, before_length, after_start, after_length)
            ON document_comparison_word_edits TO civic_lens_public_owner;
        GRANT SELECT (draft_id, revision_number, position, citation_extraction_id, citation_start, citation_length, is_change_date_evidence)
            ON document_change_revision_citations TO civic_lens_public_owner;
        GRANT SELECT (draft_id, revision_number, issue_id, position) ON document_change_revision_issues TO civic_lens_public_owner;
        GRANT SELECT (draft_id, revision_number, official_id, position) ON document_change_revision_officials TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, "position", source_id, document_number) ON executive_action_revision_notices TO civic_lens_public_owner;
        GRANT SELECT (source_id, document_number, published_on, notice_url) ON executive_action_documents TO civic_lens_public_owner;
        GRANT SELECT (zcta, state, district, land_area_square_meters, attempt_id) ON zcta_districts TO civic_lens_public_owner;
        GRANT SELECT (release_number, official_id, name, wikidata_id, website, photo_path) ON release_officials TO civic_lens_public_owner;
        GRANT SELECT (release_number, official_id, "position", office, kind, state, district, party, caucus, start_on, end_on)
            ON release_official_terms TO civic_lens_public_owner;
        GRANT SELECT (release_number, issue_id, name, definition, count_congress, substantive_votes, bills) ON release_issues TO civic_lens_public_owner;
        GRANT SELECT (release_number, issue_id, "position", text) ON release_issue_includes TO civic_lens_public_owner;
        GRANT SELECT (release_number, issue_id, "position", text) ON release_issue_excludes TO civic_lens_public_owner;
        GRANT SELECT (release_number, policy_area_id, name) ON release_policy_areas TO civic_lens_public_owner;
        GRANT SELECT (attempt_id) ON roster_snapshots TO civic_lens_public_owner;
        GRANT SELECT (attempt_id, source_id, capture_sha256, observed_at_utc_ticks) ON collection_attempts TO civic_lens_public_owner;

        GRANT SELECT (detail_job_id, projection_version, public_law, observed_at_utc_ticks, action_count) ON published_bill_histories TO civic_lens_public_owner;
        GRANT SELECT (release_number, record_id, detail_job_id, projection_version) ON release_bill_histories TO civic_lens_public_owner;
        GRANT SELECT (detail_job_id, projection_version, position, action_date, action_time, text, type, action_code, source_system_code, source_system_name)
            ON published_bill_history_actions TO civic_lens_public_owner;

        GRANT USAGE ON SCHEMA public_api TO civic_lens_public;
        GRANT SELECT ON public_api.active_records, public_api.active_release, public_api.votes, public_api.vote_members, public_api.member_votes, public_api.vote_issues,
            public_api.bills, public_api.bill_actions, public_api.bill_sponsorships, public_api.briefs, public_api.brief_paragraphs, public_api.brief_citations, public_api.issue_votes, public_api.issue_sponsors, public_api.bill_issues, public_api.zip_districts,
            public_api.party_agreement, public_api.release_officials, public_api.release_official_terms, public_api.release_issues, public_api.release_issue_includes,
            public_api.release_issue_excludes, public_api.release_policy_areas, public_api.officials, public_api.official_attendance,
            public_api.official_party_lines, public_api.official_bill_counts, public_api.official_issue_counts, public_api.official_executive_actions,
            public_api.bill_status, public_api.record_headlines, public_api.record_links, public_api.vote_facts,
            public_api.executive_actions, public_api.executive_action_notices, public_api.withdrawals, public_api.withdrawal_corrections,
            public_api.vote_first_publications, public_api.vote_party_tallies, public_api.vote_evidence, public_api.vote_sources TO civic_lens_public;
        GRANT SELECT ON public_api.statements, public_api.statement_speakers TO civic_lens_public;
        GRANT SELECT ON public_api.statement_sources, public_api.statement_quotations TO civic_lens_public;
        GRANT SELECT ON public_api.document_changes, public_api.document_change_officials, public_api.document_change_sources,
            public_api.document_change_hunks, public_api.document_change_word_edits, public_api.document_change_citations TO civic_lens_public;
        """;

    public static Task ApplyAsync(DbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(Sql, cancellationToken);
}
