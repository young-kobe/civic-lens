# Survey: serve votes, bills and the official page through PublicApi (R-68)

Read-only survey. No code changed. No database was touched.

Base: `origin/main` at `de620566`. The open view PR is `origin/public-api-views` at `ec6024ca`. In this file, `PR:` means that branch. A line number after `PR:` counts lines in the file as the branch has it.

The owner choices are fixed: role `civic_lens_public`, views in the operational database, keyset paging with a 50-row cap, the release number in each answer, and one `public_api.active_records` view that every view joins.

## What exists today

- The PR adds four views: `active_records`, `votes`, `vote_members`, `vote_issues` (`PR:src/CivicLens.Infrastructure/Collection/Migrations/20261103165431_PublicApiViews.cs:19,26,50,57`).
- `active_records` joins the one active release row with `publication_record_states` (same file, `:19-24`). The rule is `first_release <= N AND (last_release IS NULL OR last_release >= N)`.
- `votes` shows the roll call, the bill, the headline, the summary, `text_origin`, `label`, and four counts (same file, `:26-48`). A vote revision with no member list appears in no view (same file, `:48,62`).
- The label wording is a CASE in SQL that copies `PublishedRecordLabels` (same file, `:29-32`).
- The role has `CONNECTION LIMIT 5`, a 2 second statement limit, and read-only sessions (`PR:src/CivicLens.Infrastructure/Publication/PublicApiGrants.cs:24-28`).
- The owner role holds column grants only (same file, `:30-35`). Every view column is in `PR:docs/architecture/public-api-columns.txt`.
- `PublicApi` reads none of this. It ingests release files over HTTP and keeps its own search database (`docs/architecture/search.md:9-13`, `src/CivicLens.PublicApi/PublicApiApp.cs:63-70`).
- `PublicApi` has three routes: `/v1/release`, `/v1/search`, `/v1/records/{id}` (`src/CivicLens.PublicApi/Search/SearchEndpoints.cs:14-30`).
- `PublicApi` may reference only `Publication.Contracts` and Npgsql (`AGENTS.md:21`, `tests/CivicLens.Tests/Architecture/ArchitectureTests.cs:64-70`). A third data source needs no new reference.
- Action revisions now hold columns. `action_revisions` has the bill, headline, summary, `text_origin`, policy area, and `suggestion_*` columns (`docs/architecture/schema/actions.md:124-170`). Child tables are `action_revision_sponsorships` and `action_revision_issues` (`:95-101,113-122`).
- Every action revision either has all 15 content columns or none (`ck_action_revisions_columns_written`, `docs/architecture/schema/actions.md`, the `check` line under `action_revisions`).
- A vote revision keeps the result and the question only as fact rows in `vote_revision_facts`, as text "Result: ..." and "Question: ..." (`src/CivicLens.Publication.Contracts/PublishedVote.cs:147`, `docs/architecture/votes.md:51`). Neither is a column of `votes`. The vote strip needs both.
- The vote member table has an index on `(person_id, list_id)` only (`docs/architecture/schema/votes.md:107`). The view shows `bioguide_id`, so a lookup by official has no index.

## 1. Step 21: serve votes and bills

### 1.1 Common rules

- All new routes read through one new data source that logs in as `civic_lens_public`. It never reads the search database or a base table.
- The role allows 5 connections (`PR:...PublicApiGrants.cs:24`). Set `Maximum Pool Size=4` in the connection string. One service runs for each search database (`docs/architecture/search.md:238`).
- Add the option `CIVIC_LENS_PUBLIC_VIEWS_DATABASE`. Copy the reader pattern that adds the statement timeout (`src/CivicLens.PublicApi/PublicApiOptions.cs:32-38`).
- Each answer holds `release`. The value is `active_records.release_number`. One query reads rows and release together, so they cannot differ.
- Page size: default 20, at most 50. This is `SearchRequest.MaximumLimit = PublicationProtocol.PageSize` (`src/CivicLens.PublicApi/Search/SearchRequest.cs:10-11`, `src/CivicLens.Publication.Contracts/PublicationProtocol.cs:14`).
- Errors use problem JSON from `ApiResults` (`docs/architecture/search.md:141`). A bad parameter gives 400. A release change inside a walk gives 409 (decision D1).
- The views show the active release only (`PR:docs/architecture/publication.md:584`). So these routes take no `release` pin that differs from the active one.
- Every response keeps `Access-Control-Allow-Origin: *` and no cookie (`src/CivicLens.PublicApi/PublicApiApp.cs:98-102`).

### 1.2 Vote endpoints

| Path | Query parameters | Response | Reads |
|---|---|---|---|
| `GET /v1/votes` | `chamber`, `congress`, `session`, `kind`, `billType`, `billNumber`, `issue`, `from`, `to`, `cursor`, `limit` | `{release, votes: [VoteRow], nextCursor}` | `votes`, and `vote_issues` for `issue` |
| `GET /v1/votes/{recordId}` | none | `{release, vote: VoteRow, issues: [{issueId, isTracked}]}` | `votes`, `vote_issues` |
| `GET /v1/votes/{recordId}/members` | `cast`, `cursor`, `limit` | `{release, members: [{position, bioguideId, cast}], nextCursor}` | `vote_members` |

`VoteRow` is: `recordId`, `revisionNumber`, `chamber`, `congress`, `session`, `rollCall`, `votedOn`, `kind`, `billType`, `billNumber`, `headline`, `summary`, `textOrigin`, `label`, `question`, `result`, `yea`, `nay`, `present`, `notVoting`.

- Order of `/v1/votes`: `voted_on` desc, `chamber` asc, `session_number` desc, `roll_call_number` desc, `record_id` asc. This is the order of `PublishedOfficialVotes.Of` (`src/CivicLens.Publication.Contracts/PublishedOfficialVotes.cs:16-17`).
- Order of members: `position` asc. The key is `(record_id, revision_number, position)`. A Senate or House list holds at most 600 members (`PublishedVote.MaximumMembers`). A full House vote needs 9 pages.
- `question` and `result` are new view columns (decision D3). `votes` has neither today.
- The counts are null when a cast is a candidate name (`PR:...PublicApiViews.cs:36-41`). The response keeps the null.
- `textOrigin` and `label` travel together. A `Model` text always has its label. A `Person` text has none. The view guarantees this (`PR:docs/architecture/publication.md:304`).
- Members show `bioguideId` only. The views hide person IDs (`PR:docs/architecture/publication.md:587`). The page needs names (decision D8).
- The cursor holds the release and the record ID, as the sorted search cursor does (`src/CivicLens.PublicApi/Search/SearchCursor.cs:8-11`). The query reads the key of that record in the same view (decision D4).

### 1.3 Bill endpoints

| Path | Query parameters | Response | Reads |
|---|---|---|---|
| `GET /v1/bills` | `congress`, `billType`, `billNumber`, `sponsor` (bioguide ID), `issue`, `policyArea`, `from`, `to`, `cursor`, `limit` | `{release, bills: [BillRow], nextCursor}` | `bills`, `bill_issues`, `bill_sponsorships` |
| `GET /v1/bills/{recordId}` | none | `{release, bill: BillRow, issues: [issueId]}` | `bills`, `bill_issues` |
| `GET /v1/bills/{recordId}/sponsorships` | `role`, `cursor`, `limit` | `{release, sponsorships: [...], nextCursor}` | `bill_sponsorships` |

`BillRow` is: `recordId`, `revisionNumber`, `congress`, `billType`, `billNumber`, `billTitle`, `headline`, `summary`, `textOrigin`, `label`, `policyArea`, `policyAreaId`, `eventDate`, `sponsorCount`, `cosponsorCount`.

- Order of `/v1/bills`: `event_date` desc, then `record_id` asc. `event_date` is the newest sponsored or withdrawn date, as the page and the manifest entry use it (`src/CivicLens.Publication.Contracts/PublishedAction.cs:43`, `docs/architecture/actions.md:33`).
- A page of bills needs the tracked issues of at most 50 records. Read them with one batch query over `bill_issues` for the page record IDs, not one query for each bill (`AGENTS.md`, "Avoid unnecessary repeated reads").
- Sponsorships can reach 600 for a bill (`PublishedAction.MaximumSponsorships`, `PublishedAction.cs:5`). They get their own paged route. The detail answer holds only the counts.

### 1.4 What changes in PublicApi

Today `PublicApi`:

- reads `release.json` and record files over HTTP every 30 seconds (`docs/architecture/search.md:13`);
- writes them to the search database (`PublicApiApp.cs:63,73-74`);
- answers search and record lookups from that database (`SearchEndpoints.cs:14-30`);
- stores reader reports there (`PublicApiApp.cs:76`).

After step 21:

- Added: the third data source, the option, the vote and bill routes, one cursor helper, and the cache header helper.
- Kept: ingest, the search database, `/v1/search`, `/v1/records/{id}`, and reports. Full-text search needs body text, and the views carry none (`docs/architecture/search.md:27`).
- Stopped: nothing stops in step 21. The vote and bill routes never touch the release files or the search database.
- Stops later (roadmap, last item of R-68): the release manifest as a data path (`docs/roadmap.md:135`, `docs/architecture/publication.md:683`). That change belongs to the page rebuild PRs, not to step 21.
- Two sources of "active release" now exist: `active_release` in the search database (set by a 30 second poll) and `publication_releases.is_active`. They can differ for up to one poll. See D2.

### 1.5 Proposed bill views

All follow the vote precedent. Each joins `public_api.active_records` with `kind = 'action'`. Each is `security_barrier`. Each is owned by `civic_lens_public_owner` and gets one SELECT grant to `civic_lens_public` (`PR:...PublicApiViews.cs:19-62`, `PR:...PublicApiGrants.cs:30-38`).

**The unmoved-revision filter.** An old revision has only `revision_json` and no columns. The vote views skip it with `member_list_id IS NOT NULL` (`PR:...PublicApiViews.cs:48`). For actions, the check `columns_written` makes all 15 content columns null together. So the filter is `r.headline IS NOT NULL`. Add the same kind of test as the vote one: `AVoteRevisionWithNoWrittenColumnsAppearsInNoVoteViewBecauseItHoldsNothingToShow` (`PR:docs/architecture/publication.md:638`). Also require one sponsorship row, because a published action has 1 to 600 (`PublishedAction.cs:65-66`).

**`public_api.bills`**

```sql
CREATE VIEW public_api.bills WITH (security_barrier = true) AS
SELECT a.record_id, a.revision_number, a.release_number,
       r.congress, r.bill_type, r.bill_number, r.bill_title,
       r.headline, r.summary, r.text_origin,
       CASE r.text_origin
         WHEN 'Model' THEN 'An AI summary of the official record'
         WHEN 'Code'  THEN 'Written automatically from Congress.gov data, with no AI'
       END AS label,
       r.policy_area, r.policy_area_id,
       s.event_date, s.sponsor_count, s.cosponsor_count
  FROM public_api.active_records a
  JOIN public.action_revisions r
    ON r.record_id = a.record_id AND r.revision_number = a.revision_number
 CROSS JOIN LATERAL (
       SELECT max(GREATEST(p.sponsored_on, COALESCE(p.withdrawn_on, p.sponsored_on))) AS event_date,
              count(*) FILTER (WHERE p.role = 'Sponsor') AS sponsor_count,
              count(*) FILTER (WHERE p.role = 'Cosponsor' AND p.withdrawn_on IS NULL) AS cosponsor_count
         FROM public.action_revision_sponsorships p
        WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number) s
 WHERE a.kind = 'action' AND r.headline IS NOT NULL AND s.event_date IS NOT NULL;
```

- The `event_date` formula copies `PublishedSponsorship.LatestDate` (`src/CivicLens.Publication.Contracts/PublishedSponsorship.cs`, last property). The test must compare it with `PublishedAction.ToEntry().EventDate`, as the vote test compares `VoteTally`.
- The cosponsor count copies `CosponsorIdsOfEntry` except for the "also a sponsor" rule and the distinct people rule (`PublishedAction.cs:47-53`). The test must show equal counts, or the view must apply both rules. This is an open risk, listed in D6.
- The wording CASE repeats `PublishedRecordLabels`. Add a test as the vote one does (`PR:docs/architecture/publication.md:304`).

**`public_api.bill_sponsorships`**

```sql
SELECT a.record_id, a.revision_number, p."position", p.bioguide_id, p.role,
       p.is_original, p.sponsored_on, p.withdrawn_on
  FROM public_api.active_records a
  JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
  JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number
 WHERE a.kind = 'action' AND r.headline IS NOT NULL;
```

**`public_api.bill_issues`**

```sql
SELECT a.record_id, a.revision_number, i.issue_id
  FROM public_api.active_records a
  JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
  JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number
 WHERE a.kind = 'action' AND r.headline IS NOT NULL AND i.is_tracked;
```

The page publishes tracked issues only (`PublishedAction.TrackedIssueIds`, `PublishedAction.cs:22`). See D7.

**Public fields**: bill identity, title, headline, summary, `text_origin`, `label`, policy area name and ID, sponsor and cosponsor dates, bioguide ID, role, `is_original`, the two counts, the event date.

**Never public**:

- `suggestion_headline`, `suggestion_summary`, `suggestion_run_id`, `suggestion_fingerprint`. The suggestion columns are model drafts, not approved text.
- Notes: review decision notes and correction notes. No view joins them.
- `author_subject`, `author_kind`, `run_id`, `candidate_*`, `fingerprint`, `subjects_hash`, `issue_revision_id`, `issue_candidates_recorded`, `source_id`, `summary_version`.
- `revision_json`, `created_at_utc_ticks`.
- All `*_citation_*` columns and `person_id`. The vote views also hide them (`PR:docs/architecture/publication.md:587`). Evidence display is planned work (`docs/architecture/publication.md:698` on the branch).

The owner role then gets column grants for exactly these columns (`PR:...PublicApiGrants.cs:30-35` is the model). Add each view column to `public-api-columns.txt` in the same change.

## 2. Step 22: the official page with a vote strip

### 2.1 What the strip shows

One row for each recent vote of one official: date, the question, the result, the cast, the headline, and a link to the vote page. The static site builds this from `PublishedOfficialVotes` (`src/CivicLens.Publication.Contracts/PublishedOfficialVote.cs`, `PublishedOfficialVotes.cs:6-17`), with up to 5,000 rows for each Congress.

### 2.2 View and endpoint

Add one view, `public_api.official_votes`, and one route.

```sql
CREATE VIEW public_api.official_votes WITH (security_barrier = true) AS
SELECT e.bioguide_id, v.record_id, v.revision_number, a.release_number,
       v.voted_on, v.chamber, v.congress, v.session_number, v.roll_call_number,
       v.kind, v.bill_type, v.bill_number, v.headline, q.question, q.result, e.vote_cast
  FROM public_api.active_records a
  JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
  JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
  LEFT JOIN LATERAL ( ...question and result from vote_revision_facts... ) q ON true
 WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
```

- It leaves out the totals and the summary. The strip does not show them, and the `votes` view counts every member of every list for each row (`PR:...PublicApiViews.cs:36-42`). A separate view avoids that cost.
- It joins on `bioguide_id`, so it needs the new index in section 3.
- `GET /v1/officials/{bioguideId}/votes?congress=&kind=&cursor=&limit=` returns `{release, bioguideId, votes: [{recordId, votedOn, chamber, congress, session, rollCall, billType, billNumber, headline, kind, question, result, cast}], nextCursor}`.
- Order: the same as `/v1/votes`. The cursor holds the release and the record ID.
- The route returns the raw cast. The shared component maps it to a side with the rule of `PublishedOfficialVote.SideOf` (`PublishedOfficialVote.cs:31-36`). That rule has one C# owner today. A second copy in `public.js` is a second owner. See D9.
- Party and party majority are not in the view. The roster tables hold the party. The strip does not need them.

### 2.3 Paging and caching rules

- The server-rendered official page holds the first 10 votes. The release renders it once. Cloudflare caches the page for that release.
- The page embeds its release number in a data attribute. The script calls `/v1/officials/{id}/votes?release=<N>&limit=50`.
- The API answers 200 only when `N` equals the active release. Otherwise it answers 409 with the active release number. The script then shows "This page has a newer version" with a reload link.
- A 200 answer for release N never changes, because the data of a release is fixed. Send `Cache-Control: public, max-age=60`, the same rule as the static files (`docs/operations.md:993`). The URL holds `release`, so the cache key is per release.
- A 409 answer and any 5xx answer send `no-store`.
- Each later page uses the cursor from the answer. The cursor holds the release, so a walk cannot mix two releases (`SearchRequest.Parse` already refuses a cursor and a release that differ, `SearchRequest.cs:38-41`).
- A reader without script sees the first 10 votes and a link to the Congress vote page. The facet and static pages stay the path for those readers (`docs/architecture/search.md:29`).
- Worst case for one official: 5,000 votes in a Congress is 100 pages of 50. The strip loads a page only on a click.
- A new insight follows the same recipe: one view, one route, one shared component (`docs/roadmap.md:135`).

## 3. Load on the shared database

The role reads the operational database that the worker writes. Reads use MVCC and do not block the worker. The risks are CPU, I/O, buffer cache, the 5 connection limit, and any DDL lock from a migration.

**Run these on a restored copy or a staging database, as the role `civic_lens_public`. Do not run them on production.** Use `EXPLAIN (ANALYZE, BUFFERS, SETTINGS)`.

### 3.1 Size and shape of the data

```sql
SELECT relname, n_live_tup, pg_size_pretty(pg_total_relation_size(relid))
  FROM pg_stat_user_tables
 WHERE relname IN ('publication_record_states','publication_releases','vote_revisions',
                   'vote_member_list_entries','vote_member_lists','vote_revision_facts',
                   'vote_revision_issues','action_revisions','action_revision_sponsorships',
                   'action_revision_issues')
 ORDER BY pg_total_relation_size(relid) DESC;

SELECT kind, count(*) FILTER (WHERE last_release IS NULL) AS open_ranges, count(*) AS all_ranges
  FROM publication_record_states GROUP BY kind;

SELECT count(DISTINCT member_list_id) AS lists, count(*) AS revisions FROM vote_revisions;
SELECT max(c), avg(c) FROM (SELECT count(*) c FROM action_revision_sponsorships GROUP BY record_id, revision_number) t;
```

### 3.2 Query plans

Replace the literal IDs with real ones from the copy.

```sql
-- The active release view alone. Watch the scan type of publication_record_states.
EXPLAIN (ANALYZE, BUFFERS) SELECT count(*) FROM public_api.active_records;
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM public_api.active_records WHERE kind = 'vote' LIMIT 50;

-- First page of votes. Check that LIMIT stops the lateral count early.
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.votes
 ORDER BY voted_on DESC, chamber, session_number DESC, roll_call_number DESC, record_id
 LIMIT 51;

-- Keyset page. $1.. come from the last row of the first page.
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.votes
 WHERE (voted_on, session_number, roll_call_number) < ('2026-03-01', 1, 120)
 ORDER BY voted_on DESC, chamber, session_number DESC, roll_call_number DESC, record_id
 LIMIT 51;

-- A filter by issue and one by bill.
EXPLAIN (ANALYZE, BUFFERS)
SELECT v.* FROM public_api.votes v
 WHERE EXISTS (SELECT 1 FROM public_api.vote_issues i
                WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.issue_id = 'health')
 ORDER BY v.voted_on DESC LIMIT 51;
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM public_api.votes WHERE bill_type = 'hr' AND bill_number = 1;

-- Members of one vote. A House vote has about 440 rows.
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.vote_members WHERE record_id = '<vote record id>' ORDER BY "position" LIMIT 51;

-- The vote strip of one official. This one should use a bioguide index.
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.vote_members m
  JOIN public_api.votes v USING (record_id, revision_number)
 WHERE m.bioguide_id = 'B000000'
 ORDER BY v.voted_on DESC LIMIT 51;
-- After the new view exists:
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.official_votes WHERE bioguide_id = 'B000000'
 ORDER BY voted_on DESC, chamber, session_number DESC, roll_call_number DESC, record_id LIMIT 51;

-- Bills, after the bill views exist.
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM public_api.bills ORDER BY event_date DESC, record_id LIMIT 51;
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM public_api.bills WHERE congress = 119 AND bill_type = 'hr' AND bill_number = 1;
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM public_api.bill_sponsorships WHERE record_id = '<action record id>' ORDER BY "position" LIMIT 51;
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.bill_issues WHERE (record_id, revision_number) IN (('<id1>',1),('<id2>',1) /* up to 50 pairs */);
```

What to read in each plan:

- Does `Limit` sit above the lateral subquery? If a full sort runs before it, the count of every vote runs (about 440 member rows for each vote).
- Is a filter pushed below the view? A `security_barrier` view blocks the push of any qual whose operator is not leakproof. Equality and range on integers, dates, and text normally pass.
- `Rows Removed by Filter`, `Heap Fetches`, and `Buffers: shared read` against `shared hit`.
- A rollback test: activate an older release and run the `active_records` plan again. Open ranges are not a fixed set then.

### 3.3 Indexes to test

```sql
-- Needed for the official lookup. The existing index is on person_id, not bioguide_id.
CREATE INDEX CONCURRENTLY ix_vote_member_list_entries_bioguide
    ON vote_member_list_entries (bioguide_id, list_id);

-- Ordering and filters of the vote list. vote_revisions has no index on voted_on.
CREATE INDEX CONCURRENTLY ix_vote_revisions_voted_on
    ON vote_revisions (voted_on DESC, chamber, session_number DESC, roll_call_number DESC, record_id);

-- Bill lookups by identity and by order.
CREATE INDEX CONCURRENTLY ix_action_revisions_bill
    ON action_revisions (congress, bill_type, bill_number);

-- Result and question lookups by revision and prefix.
CREATE INDEX CONCURRENTLY ix_vote_revision_facts_prefix
    ON vote_revision_facts (record_id, revision_number, left(fact_text, 8));

-- Sponsor filter. Exists for person_id, not bioguide_id.
CREATE INDEX CONCURRENTLY ix_action_revision_sponsorships_bioguide
    ON action_revision_sponsorships (bioguide_id, record_id, revision_number);
```

- Existing: `ix_vote_member_list_entries_person (person_id, list_id)`, `IX_vote_revisions_member_list_id`, `ix_action_revision_sponsorships_person`, `IX_publication_record_states_first_release` and `_last_release` (`docs/architecture/schema/votes.md:107,190`, `schema/actions.md:121`, `schema/publication.md:12-13`).
- Judge each new index with the plans above and with `pg_relation_size`. Each index adds cost to every worker write to that table.
- `CREATE INDEX CONCURRENTLY` cannot run inside a migration transaction. A migration needs `suppressTransaction: true`. This is a schema decision for the owner (`docs/agent-rules.md:16`).

### 3.4 Load and effect on the worker

```sql
-- Statements of the role, if pg_stat_statements is enabled.
SELECT left(query, 80) AS query, calls, round(mean_exec_time::numeric, 2) AS mean_ms,
       round(max_exec_time::numeric, 2) AS max_ms, shared_blks_hit, shared_blks_read
  FROM pg_stat_statements s JOIN pg_roles r ON r.oid = s.userid
 WHERE r.rolname = 'civic_lens_public'
 ORDER BY total_exec_time DESC LIMIT 20;

-- Connection use and waits while the load runs.
SELECT usename, state, wait_event_type, wait_event, count(*)
  FROM pg_stat_activity WHERE datname = current_database() GROUP BY 1,2,3,4 ORDER BY 5 DESC;

-- Lock waits of the worker during the test.
SELECT l.pid, l.mode, l.granted, a.query
  FROM pg_locks l JOIN pg_stat_activity a USING (pid) WHERE NOT l.granted;

-- Cache and I/O.
SELECT relname, heap_blks_read, heap_blks_hit, idx_blks_read, idx_blks_hit
  FROM pg_statio_user_tables WHERE relname IN ('vote_revisions','vote_member_list_entries','publication_record_states');
```

Load script, for a copy only (`pgbench` with the role, 5 clients, one file for each query of 3.2):

```sh
pgbench -n -c 4 -j 2 -T 60 -P 10 -f votes_page.sql  "$COPY_URL_AS_PUBLIC_ROLE"
pgbench -n -c 4 -j 2 -T 60 -P 10 -f official_strip.sql "$COPY_URL_AS_PUBLIC_ROLE"
```

Run each with the worker idle, then with a publish and an analysis stage running. Compare the p95 of the worker queries before and after. Record the result in the Limits section of `docs/architecture/publication.md`, not in a report file (`AGENTS.md`, documentation rules).

Capacity estimate: the limit is 4 pool connections. If a query takes 20 ms, one connection serves 50 queries a second, so the pool serves about 200 a second. Cloudflare serves a cached URL from the edge. The origin sees about one request for each distinct URL, edge location, and 60 seconds.

## 4. PR order

Each PR starts from `origin/main` (`docs/agent-rules.md:7`). A PR that needs an earlier one waits for its merge. The owner merges.

| PR | Content | Needs |
|---|---|---|
| 0 | The open `public-api-views` PR: schema, role, four views. | none |
| 1 | Vote view columns `question` and `result`, the bioguide index, columns file, grants for `vote_revision_facts`, tests. | 0 |
| 2 | Bill views `bills`, `bill_sponsorships`, `bill_issues`, grants, columns file, label and event date tests, the unmoved-revision test. | 1 (same grants and columns files) |
| 3 | `PublicApi` plumbing with its first user: the views database option, pool limit, cursor helper, `GET /v1/votes` and `GET /v1/votes/{id}`. Docs and architecture test update. | 1 |
| 4 | `GET /v1/votes/{id}/members`. | 3 |
| 5 | `GET /v1/bills`, `/v1/bills/{id}`, `/v1/bills/{id}/sponsorships`. | 2, 3 |
| 6 | Measurement on a copy with the SQL of section 3. Index decisions. Results into `publication.md` Limits. Index migration if the plans need it. | 3, 4, 5 |
| 7 | View `official_votes` and `GET /v1/officials/{bioguideId}/votes` with the release parameter and cache headers. | 6 |
| 8 | Shared vote strip component in `public.js`, and the server-rendered strip on the official page. Browser check at 360 and 1280 px, light and dark (`docs/agent-rules.md:34`). | 7 |

Plumbing is in PR 3 and not alone, because an added member needs a production caller (`AGENTS.md`, verification).

Every PR updates `docs/roadmap.md` (completed work leaves the list) and the feature doc in place. A migration PR regenerates the schema docs (`docs/agent-rules.md:33`).

## 5. Open decisions for the owner

### D1. A release changes while a reader walks pages

- Problem: the views show the active release only, and there is no release parameter. A reader could get page 1 from release 12 and page 2 from release 13.
- Precedent checked: the search cursor names its release, and a named release that differs is refused (`docs/architecture/search.md:22,81`, `SearchCursor.cs:8-11`).
- Options:
  - (a) The cursor holds the release. The route answers 409 with the active number when it differs. Cost: a reader restarts the walk after a publish. Benefit: no mixed pages.
  - (b) Ignore the change. Cost: a row can repeat or vanish between pages. Benefit: nothing to build.
  - (c) Make the view take a release number. Cost: a function or a parameter table, which the PR rules out (`PR:docs/architecture/publication.md:584`). Benefit: pinned reads like search.
- Recommendation: (a).

### D2. Which source names the active release

- Problem: `/v1/release` reads the search database. The new routes read the views. They can differ for up to 30 seconds.
- Precedent checked: the search service follows `release.json` by polling (`docs/architecture/search.md:13,17`). The view follows `is_active` at once (`PR:docs/architecture/publication.md:303`).
- Options:
  - (a) Keep both. Cost: a page can show two numbers.
  - (b) Move `/v1/release` to the views in PR 3. Search results keep their own release field.
  - (c) Add `releaseSource` to answers. Cost: more wording for readers.
- Recommendation: (b). The views are the single truth for R-68, and search still pins its index release.

### D3. Where `result` and `question` come from

- Problem: both are fact rows, not columns. The strip needs them.
- Precedent checked: the vote kind is stored at draft time and publication never derives it again (`docs/architecture/votes.md:49`). The label CASE repeats wording in SQL with a test (`PR:...PublicApiViews.cs:29-32`).
- Options:
  - (a) Read the fact rows in the view with a prefix match and test the result against `PublishedVote.Result`. Cost: a lateral lookup and a second owner of the prefix.
  - (b) Store `result` and `question` as columns in the vote revision, written by the draft step. Cost: contract, draft shape, and backfill. This needs your decision (`docs/agent-rules.md:16`). Benefit: one owner and the cheapest read.
  - (c) Leave them out of the strip. Cost: the strip loses two of its asked fields.
- Recommendation: (a) now, with the test. Move to (b) only if the plans in 3.2 show a cost.

### D4. Cursor form

- Problem: a keyset needs the sort key. A cursor can carry it or can carry the record ID only.
- Precedent checked: the sorted search cursor carries only the record ID and the query reads the key in the same release. A forged record ID gives an empty page (`docs/architecture/search.md:24`, `SearchCursor.cs:8-11`). Review pages carry a time and an ID (`docs/architecture/review.md:214`).
- Options:
  - (a) Record ID only. Cost: one extra key lookup in each page query. Benefit: one format for all routes.
  - (b) Full key in the cursor. Cost: a client can forge any key. Benefit: no lookup.
- Recommendation: (a).

### D5. Bill identity and grain

- Problem: bills have a record ID and also a `(congress, type, number)` identity. The path must pick one.
- Precedent checked: record pages use `records/{recordId}.html` (`PublicationProtocol.RecordPagePath`, `PublicationProtocol.cs:59`). The search route uses the record ID (`SearchEndpoints.cs:22`). The action draft's foreign key suggests one record fixes one bill (`docs/architecture/schema/actions.md`, `action_revisions` foreign keys). Confirm this.
- Options:
  - (a) Path by record ID. Filter by the bill triple.
  - (b) Path by `/v1/bills/{congress}/{type}/{number}`. Cost: a unique rule the view must keep.
  - (c) Both.
- Recommendation: (a).

### D6. Computed counts in the bill view

- Problem: the event date and the cosponsor count are computed per row, as the vote counts are.
- Precedent checked: vote counts use a lateral count and one test against `VoteTally` (`PR:...PublicApiViews.cs:36-42`). `ActionDraftRevision.EventOn` owns the date today (`docs/architecture/actions.md:33`).
- Options:
  - (a) Lateral aggregate in the view. Cost: a copy of the C# rules, and a test for each.
  - (b) Store both as columns when the revision is written. Cost: schema change and backfill.
- Recommendation: (a), because it follows the vote view. Take (b) if the plans show a slow lateral on 600-sponsorship bills. The cosponsor rules of `CosponsorIdsOfEntry` (not a sponsor, distinct people) need the same test.

### D7. Untracked issues in `vote_issues`

- Problem: `vote_issues` shows `is_tracked`, so it carries untracked issues too. The published page and `PublishedOfficialVote.IssueIds` hold tracked ones only (`PublishedOfficialVote.cs:23`, `PublishedAction.cs:22`).
- Precedent checked: the PR view exposes the flag (`PR:...PublicApiViews.cs:57-62`).
- Options:
  - (a) Keep the flag. The route filters. Cost: a route bug could show an untracked issue.
  - (b) Filter in the view, as proposed for `bill_issues`. Cost: changes `public-api-columns.txt` and the PR's view.
- Recommendation: (b) for the bill view in PR 2. Ask whether to change `vote_issues` the same way. What a view never shows cannot leak.

### D8. Names and party of members

- Problem: `vote_members` has `bioguide_id` only. A vote page needs names and parties. The roster lives in other tables and in `officials.json`.
- Precedent checked: the PR hides person IDs and citations (`PR:docs/architecture/publication.md:587`). R-68 says no JSON file carries data to the public path (`docs/roadmap.md:135`).
- Options:
  - (a) The page joins names from the release at render time. Cost: keeps a JSON data path for now.
  - (b) A new view `officials` over the roster tables. Cost: a new view and grants. Benefit: one source.
  - (c) Return names inside member rows. Cost: a join and a name source for each member row.
- Recommendation: (b), as a later PR. Step 21 returns `bioguideId` only. Step 22 needs no names, because the page knows the official.

### D9. Where the cast to side rule lives

- Problem: "Yea or Aye is yea" lives in C# (`PublishedOfficialVote.SideOf`, `PublishedOfficialVote.cs:31-36`) and in SQL (`PR:...PublicApiViews.cs:36-41`). A browser component would add a third copy.
- Precedent checked: the PR already accepts a tested SQL copy for counts (`PR:docs/architecture/publication.md:304`).
- Options:
  - (a) The route returns the cast and a `side` column from the view (`yea`, `nay`, or null). The component shows the field. One more tested SQL copy, no browser rule.
  - (b) The component maps the cast. A third copy with no SQL test.
- Recommendation: (a). Add `side` to `official_votes` and test it against `SideOf`.

### D10. Cache key for the API

- Problem: Cloudflare caches by URL. A first page with no cursor has no release in its URL.
- Precedent checked: files have `public, max-age=60` and the operator purges by hand (`docs/operations.md:786,993`).
- Options:
  - (a) The page embeds its release and the script sends `release=N`. The API answers 409 when N is not active. Cache per release at the edge. Cost: one extra branch for readers of an old page.
  - (b) No release in the URL, a short `s-maxage`. Cost: up to a minute of old data and mixed pages across pages of one walk.
  - (c) Purge by tag at each publish. Cost: a Cloudflare credential and a new deploy step. This breaks the rule that cloud agents hold no secrets for production (`docs/agent-rules.md:43`) only if an agent touches it, but needs owner work.
- Recommendation: (a).

### D11. Index migrations

- Problem: the new indexes must not block worker writes on large tables.
- Precedent checked: migrations run in `db migrate` with a 10 minute limit (`docs/operations.md` migrate paragraph, `PR:...PostgresCollectionAttemptStore.cs` `MigrateAsync`).
- Options:
  - (a) Plain `CREATE INDEX` in a transaction. Blocks writes during the build. Simple.
  - (b) `CONCURRENTLY` with `suppressTransaction`. No write block. A failed build leaves an invalid index to drop by hand.
  - (c) Build the index by hand before the migration. Drift between environments.
- Recommendation: (b) after PR 6 shows which index pays. Tables are small today, so (a) may be enough. Measure first.
