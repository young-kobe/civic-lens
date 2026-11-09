using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Votes;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Fixtures;

/// <summary>SQL that fills the columns and child rows of a seeded vote revision, so that the readers can rebuild it. Run it in the same batch
/// as the revision insert, after the batch has relaxed the foreign keys.</summary>
internal static class VoteRevisionColumnsSeed
{
    /// <summary>The columns that every vote revision row must set. A test that checks another rule inserts a row with <see cref="PlaceholderValues"/>.</summary>
    public const string RequiredColumns = """
        headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
        candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length, kind,
        member_list_id, issue_revision_id, issue_candidates_recorded, result
        """;

    /// <summary>One valid value for each column of <see cref="RequiredColumns"/>, in the same order. The roll call is House 119, session 1, roll 100.</summary>
    public static readonly string PlaceholderValues = $"""
        'Headline', 'Summary', 'Person', 'congress-house-votes', 'CongressGov', 'House', 119, 1, 100, 'placeholder-candidate',
        1, DATE '2026-01-02', 'placeholder-extraction', 0, 1, 'Passage', '{new string('1', 64)}', '{new string('2', 64)}', true, 'Passed'
        """;

    /// <summary>The statement that leaves the member list of a revision incomplete, so that a reader refuses the revision. It takes the parameter <c>id</c>, the vote record ID.</summary>
    public const string RemoveMemberEntries = """
        DELETE FROM vote_member_list_entries WHERE list_id IN (SELECT member_list_id FROM vote_revisions WHERE record_id = @id)
        """;

    public const string Sql = """
        ;
        INSERT INTO vote_member_lists (list_id, member_count, yea_count, nay_count, present_count, not_voting_count, other_count, created_at_utc_ticks) VALUES (@seed_list, @seed_count, @seed_yea, @seed_nay, @seed_present, @seed_not_voting, @seed_other, 1) ON CONFLICT DO NOTHING;
        INSERT INTO vote_member_list_entries (list_id, position, person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
        SELECT @seed_list, entry.position, entry.person_id, entry.bioguide_id, entry.vote_cast, entry.extraction_id, entry.start, entry.length
        FROM unnest(@seed_positions, @seed_persons, @seed_bioguides, @seed_casts, @seed_extractions, @seed_starts, @seed_lengths)
            AS entry(position, person_id, bioguide_id, vote_cast, extraction_id, start, length)
        ON CONFLICT DO NOTHING;
        UPDATE vote_revisions SET headline = @seed_headline, summary = @seed_summary, text_origin = @seed_text_origin,
            suggestion_headline = @seed_suggestion_headline, suggestion_summary = @seed_suggestion_summary, suggestion_run_id = @seed_suggestion_run,
            suggestion_fingerprint = @seed_suggestion_fingerprint, source_id = @seed_source_id, vote_source = @seed_vote_source, chamber = @seed_chamber,
            congress = @seed_congress, session_number = @seed_session, roll_call_number = @seed_roll, bill_type = @seed_bill_type, bill_number = @seed_bill_number,
            candidate_id = @seed_candidate, candidate_update_utc_ticks = @seed_candidate_ticks, voted_on = @seed_voted_on,
            voted_on_citation_extraction_id = @seed_voted_on_extraction, voted_on_citation_start = @seed_voted_on_start, voted_on_citation_length = @seed_voted_on_length,
            kind = @seed_kind, kind_citation_extraction_id = @seed_kind_extraction, kind_citation_start = @seed_kind_start, kind_citation_length = @seed_kind_length,
            member_list_id = @seed_list, issue_revision_id = @seed_issue_revision, subjects_hash = @seed_subjects_hash,
            issue_candidates_recorded = @seed_issue_candidates_recorded, question = @seed_question, result = @seed_result
        WHERE record_id = @seed_record AND revision_number = @seed_number;
        DELETE FROM vote_revision_facts WHERE record_id = @seed_record AND revision_number = @seed_number;
        INSERT INTO vote_revision_facts (record_id, revision_number, position, fact_text, citation_extraction_id, citation_start, citation_length)
        SELECT @seed_record, @seed_number, fact.position, fact.fact_text, fact.extraction_id, fact.start, fact.length
        FROM unnest(@seed_fact_positions, @seed_fact_texts, @seed_fact_extractions, @seed_fact_starts, @seed_fact_lengths)
            AS fact(position, fact_text, extraction_id, start, length);
        DELETE FROM vote_revision_issues WHERE record_id = @seed_record AND revision_number = @seed_number;
        INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked)
        SELECT @seed_record, @seed_number, issue.issue_id, issue.position, issue.is_tracked
        FROM unnest(@seed_issue_ids, @seed_issue_positions, @seed_issue_tracked) AS issue(issue_id, position, is_tracked)
        """;

    public static (string Name, object Value)[] Parameters(VoteDraftRevision vote)
    {
        var members = vote.Members;
        var counts = VoteMemberListSeed.Counts(members.Select(member => member.Cast));
        var issueIds = vote.IssueCandidateIds.IsDefault ? vote.TrackedIssueIds : vote.IssueCandidateIds;
        return
        [
            ("seed_list", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', members.Select(member => member.PersonId + member.Cast).Prepend(vote.RecordId + vote.RevisionNumber)))))),
            ("seed_count", (short)members.Length),
            ("seed_yea", counts.Yea),
            ("seed_nay", counts.Nay),
            ("seed_present", counts.Present),
            ("seed_not_voting", counts.NotVoting),
            ("seed_other", counts.Other),
            ("seed_positions", Enumerable.Range(0, members.Length).Select(position => (short)position).ToArray()),
            ("seed_persons", members.Select(member => member.PersonId).ToArray()),
            ("seed_bioguides", members.Select(member => member.BioguideId).ToArray()),
            ("seed_casts", members.Select(member => member.Cast).ToArray()),
            ("seed_extractions", members.Select(member => member.Citation.ExtractionId).ToArray()),
            ("seed_starts", members.Select(member => member.Citation.Start).ToArray()),
            ("seed_lengths", members.Select(member => member.Citation.Length).ToArray()),
            ("seed_headline", vote.Headline),
            ("seed_summary", vote.Summary),
            ("seed_text_origin", vote.TextOrigin.ToString()),
            ("seed_suggestion_headline", Db(vote.Suggestion?.Headline)),
            ("seed_suggestion_summary", Db(vote.Suggestion?.Summary)),
            ("seed_suggestion_run", Db(vote.Suggestion?.RunId)),
            ("seed_suggestion_fingerprint", Db(vote.Suggestion?.Fingerprint)),
            ("seed_source_id", vote.SourceId),
            ("seed_vote_source", vote.Source.ToString()),
            ("seed_chamber", vote.Chamber.ToString()),
            ("seed_congress", vote.Congress),
            ("seed_session", vote.SessionNumber),
            ("seed_roll", vote.RollCallNumber),
            ("seed_bill_type", Db(vote.BillType)),
            ("seed_bill_number", Db(vote.BillNumber)),
            ("seed_candidate", vote.CandidateId),
            ("seed_candidate_ticks", vote.CandidateUpdateDate.UtcTicks),
            ("seed_voted_on", vote.VotedOn),
            ("seed_voted_on_extraction", vote.VotedOnCitation.ExtractionId),
            ("seed_voted_on_start", vote.VotedOnCitation.Start),
            ("seed_voted_on_length", vote.VotedOnCitation.Length),
            ("seed_kind", vote.Kind.ToString()),
            ("seed_kind_extraction", Db(vote.KindCitation?.ExtractionId)),
            ("seed_kind_start", Db(vote.KindCitation?.Start)),
            ("seed_kind_length", Db(vote.KindCitation?.Length)),
            ("seed_issue_revision", vote.IssueRevisionId),
            ("seed_subjects_hash", Db(vote.SubjectsHash)),
            ("seed_issue_candidates_recorded", !vote.IssueCandidateIds.IsDefault),
            ("seed_question", Db(VoteFacts.QuestionOf(vote.Facts))),
            ("seed_result", VoteFacts.ResultOf(vote.RecordId, vote.Facts)),
            ("seed_record", vote.RecordId),
            ("seed_number", vote.RevisionNumber),
            ("seed_fact_positions", Enumerable.Range(0, vote.Facts.Length).Select(position => (short)position).ToArray()),
            ("seed_fact_texts", vote.Facts.Select(fact => fact.Text).ToArray()),
            ("seed_fact_extractions", vote.Facts.Select(fact => fact.Citation.ExtractionId).ToArray()),
            ("seed_fact_starts", vote.Facts.Select(fact => fact.Citation.Start).ToArray()),
            ("seed_fact_lengths", vote.Facts.Select(fact => fact.Citation.Length).ToArray()),
            ("seed_issue_ids", issueIds.ToArray()),
            ("seed_issue_positions", Enumerable.Range(0, issueIds.Length).Select(position => (short)position).ToArray()),
            ("seed_issue_tracked", issueIds.Select(id => vote.TrackedIssueIds.Contains(id)).ToArray())
        ];
    }

    private static object Db(object? value) => value ?? DBNull.Value;
}
