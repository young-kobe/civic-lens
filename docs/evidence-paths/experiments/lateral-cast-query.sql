-- Correlated LATERAL 3-hop bill-to-vote-to-cast benchmark on an active release (Setup Experiment 6).
-- Replaces the forbidden plain join that timed out at 4 seconds in published-graph.md.
-- Run with role civic_lens_readonly inside a read-only repeatable-read transaction.

BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '4s';
SET LOCAL lock_timeout = '500ms';

WITH active AS (
    SELECT release_number
    FROM publication_releases
    WHERE is_active = true
    LIMIT 1
)
SELECT ar.congress, ar.bill_type, ar.bill_number, vr.roll_call_number, count(e.person_id) AS casts
FROM active a
JOIN publication_record_states prs
  ON prs.kind = 'action'
 AND a.release_number BETWEEN prs.first_release AND COALESCE(prs.last_release, 2147483647)
JOIN action_revisions ar
  ON ar.record_id = prs.record_id AND ar.revision_number = prs.revision_number
JOIN LATERAL (
    SELECT rvb.vote_record_id
    FROM release_vote_bills rvb
    WHERE rvb.release_number = a.release_number AND rvb.bill_record_id = ar.record_id
    OFFSET 0
) vb ON true
JOIN LATERAL (
    SELECT vr2.record_id, vr2.roll_call_number, vr2.member_list_id
    FROM publication_record_states vprs
    JOIN vote_revisions vr2
      ON vr2.record_id = vprs.record_id AND vr2.revision_number = vprs.revision_number
    WHERE vprs.kind = 'vote' AND vprs.record_id = vb.vote_record_id
      AND a.release_number BETWEEN vprs.first_release AND COALESCE(vprs.last_release, 2147483647)
    OFFSET 0
) vr ON true
JOIN LATERAL (
    SELECT e2.person_id
    FROM vote_member_list_entries e2
    WHERE e2.list_id = vr.member_list_id
    OFFSET 0
) e ON true
WHERE ar.congress = 119 AND ar.bill_type = 'hr' AND ar.bill_number IN (2102, 9600)
GROUP BY 1, 2, 3, 4;

ROLLBACK;
