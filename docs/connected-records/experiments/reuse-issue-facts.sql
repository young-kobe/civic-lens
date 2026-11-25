-- Offline query example. Each input view is a small, fixed research fixture.
-- Vote keys are immutable source digests. Issue labels and comparison judgments
-- are analyst inputs. None of these rows represents a Civic Lens publication.
CREATE TEMP VIEW votes(vote_ref, day, roll, measure, motion, result) AS VALUES
 ('4dbb45cf8f2d8379e7f24e9a776793198a0d27e3ef7ba350e07025b52d3ba2b1','2026-03-05',84,'H.Res.1099','On Motion to Suspend the Rules and Agree','Passed'),
 ('430187a91c76de11bf63e6886aab14427e03ac8a0fdb018eadac81cbcec59b40','2026-03-05',85,'H.Con.Res.38','On Agreeing to the Resolution','Failed'),
 ('7e3474ac0c18abefed41bdae758f7ef99d59cfde98d26a52ad169023cdf4da27','2025-04-28',104,'S.146','On Motion to Suspend the Rules and Pass','Passed');

-- Use year and roll only to abbreviate references within these three House votes.
CREATE TEMP VIEW selected_casts(yr, roll, member, member_cast) AS VALUES
 ('2026',84,'M001184','Yea'), ('2026',85,'M001184','Yea'), ('2025',104,'M001184','Nay'),
 ('2026',84,'A000370','Yea'), ('2026',85,'A000370','Yea'), ('2025',104,'A000370','Yea');
CREATE TEMP VIEW issue_labels(yr, roll, issue) AS VALUES
 ('2026',84,'middle-east-conflicts'), ('2026',85,'middle-east-conflicts'),
 ('2026',84,'foreign-policy-and-war'), ('2026',85,'foreign-policy-and-war'),
 ('2025',104,'artificial-intelligence');

CREATE TEMP VIEW activity AS
SELECT v.vote_ref, v.day, v.roll, v.measure, v.motion, v.result, c.member, c.member_cast, i.issue
FROM votes v
JOIN selected_casts c ON c.yr = substr(v.day, 1, 4) AND c.roll = v.roll
JOIN issue_labels i ON i.yr = c.yr AND i.roll = c.roll;

-- Sample request values. Consumers can change this row without changing a view.
CREATE TEMP TABLE request(member TEXT, other_topic TEXT, poll_ref TEXT);
INSERT INTO request VALUES ('M001184','artificial-intelligence','CBS-YouGov-20260303-Q7');
CREATE TEMP TABLE followed_topics(issue TEXT PRIMARY KEY);
INSERT INTO followed_topics VALUES ('middle-east-conflicts'), ('foreign-policy-and-war');

-- Official profile. Two qualifying topics can lead to the same action.
CREATE TEMP VIEW profile_result AS
SELECT DISTINCT a.vote_ref, a.day, a.roll, a.measure, a.motion, a.result, a.member_cast
FROM activity a JOIN request r ON r.member = a.member
WHERE a.issue IN (SELECT issue FROM followed_topics);
SELECT day, roll, measure, motion, result, member_cast FROM profile_result ORDER BY day, roll;

-- Issue activity. The same view also answers a different issue and both officials.
CREATE TEMP VIEW issue_result AS
SELECT a.member, a.member_cast, a.day, a.roll, a.motion, a.vote_ref
FROM activity a JOIN request r ON r.other_topic = a.issue;
SELECT member, member_cast, day, roll, motion FROM issue_result ORDER BY member;

-- Add a new input kind without changing either preceding query.
CREATE TEMP VIEW poll_questions(ref, population, field_start, field_end, question_subject) AS VALUES
 ('CBS-YouGov-20260303-Q7','US adults','2026-03-02','2026-03-03',
  'Congressional approval for continued US military action against Iran');
CREATE TEMP VIEW poll_issues(ref, issue) AS VALUES
 ('CBS-YouGov-20260303-Q7','middle-east-conflicts'), ('CBS-YouGov-20260303-Q7','foreign-policy-and-war');
CREATE TEMP VIEW poll_responses(ref, choice, percent) AS VALUES
 ('CBS-YouGov-20260303-Q7','Needs approval',69),
 ('CBS-YouGov-20260303-Q7','Does not need approval',31);
-- The analyst compared the survey question with each legislative decision.
-- These are context judgments. Neither one establishes endorsement of a poll answer.
CREATE TEMP VIEW comparisons(poll_ref, vote_ref, scope) AS VALUES
 ('CBS-YouGov-20260303-Q7','430187a91c76de11bf63e6886aab14427e03ac8a0fdb018eadac81cbcec59b40','related authorization decision'),
 ('CBS-YouGov-20260303-Q7','4dbb45cf8f2d8379e7f24e9a776793198a0d27e3ef7ba350e07025b52d3ba2b1','shared topic only');

CREATE TEMP VIEW poll_context AS
SELECT DISTINCT a.vote_ref, a.roll, a.member_cast, q.ref, q.population, q.field_start, q.field_end, q.question_subject, c.scope
FROM activity a JOIN request r ON r.member = a.member
JOIN poll_issues t ON t.issue = a.issue AND t.ref = r.poll_ref
JOIN poll_questions q ON q.ref = t.ref
LEFT JOIN comparisons c ON c.poll_ref = q.ref AND c.vote_ref = a.vote_ref;
SELECT roll, member_cast, ref, population, field_start, field_end, question_subject, scope FROM poll_context ORDER BY roll;

-- Direction requires a separate sufficient comparison. An unassessed pair
-- would also yield no public alignment. A missing result has no numeric value.
SELECT COUNT(*) AS eligible_directional_comparisons
FROM poll_context WHERE scope = 'directly comparable';
SELECT choice, percent FROM poll_responses p JOIN request r ON r.poll_ref = p.ref ORDER BY percent DESC;

-- Change the official and interests. Both readers should point to the same
-- vote evidence for Adams and agree on her cast after the request changes.
UPDATE request SET member = 'A000370';
DELETE FROM followed_topics;
INSERT INTO followed_topics SELECT other_topic FROM request;
SELECT p.roll, p.member_cast, p.vote_ref AS shared_evidence_ref
FROM profile_result p JOIN issue_result i ON i.vote_ref = p.vote_ref
JOIN request r ON r.member = i.member
WHERE p.member_cast = i.member_cast;
