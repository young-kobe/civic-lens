# Survey: serve votes, bills and the official page through PublicApi (R-68)

Read-only survey. No code changed. No database was touched.

Base: `origin/main` at `de620566`. The open view PR is `origin/public-api-views` at `067fac0f`, with main merged in. In this file, `PR:` means that branch. A line number after `PR:` counts lines in the file as the branch has it.

The owner choices are fixed: role `civic_lens_public`, views in the operational database, keyset paging with a 50-row cap, the release number in each answer, and one `public_api.active_records` view that every view joins.

## What exists today

- The PR adds four views: `active_records`, `votes`, `vote_members`, `vote_issues` (`PR:src/CivicLens.Infrastructure/Collection/Migrations/20261103165431_PublicApiViews.cs:19,26,50,57`).
- `active_records` joins the one active release row with `publication_record_states` (same file, `:19-24`). The rule is `first_release <= N AND (last_release IS NULL OR last_release >= N)`.
- `votes` shows the roll call, the bill, the headline, the summary, `text_origin`, `label`, and four counts (same file, `:26-48`). A vote revision with no member list appears in no view (same file, `:48,62`).
- The label wording is a CASE in SQL that copies `PublishedRecordLabels` (same file, `:30-34`).
- The role has `CONNECTION LIMIT 5`, a 2 second statement limit, and read-only sessions (`PR:src/CivicLens.Infrastructure/Publication/PublicApiGrants.cs:24-28`). So a new connection string needs no timeout option of its own.
- The owner role holds column grants only (same file, `:30-35`). Every view column is in `PR:docs/architecture/public-api-columns.txt`.
- The role is created `NOLOGIN` with no password. The operator enables the login by hand (`PR:docs/operations.md:282`).
- `PublicApi` reads none of this. It ingests release files over HTTP and keeps its own search database (`docs/architecture/search.md:10-13`, `src/CivicLens.PublicApi/PublicApiApp.cs:27-28`).
- `PublicApi` has three routes: `/v1/release`, `/v1/search`, `/v1/records/{id}` (`src/CivicLens.PublicApi/Search/SearchEndpoints.cs:14-30`).
- `PublicApi` may reference only `Publication.Contracts` and Npgsql (`AGENTS.md:21`, `tests/CivicLens.Tests/Architecture/ArchitectureTests.cs:64-70`). A third data source needs no new reference.
- Action revisions now hold columns. `action_revisions` has the bill, headline, summary, `text_origin`, policy area, and `suggestion_*` columns (`docs/architecture/schema/actions.md:124-170`). Child tables are `action_revision_sponsorships` and `action_revision_issues` (`:95-101,113-122`).
- Every action revision either has all 15 content columns or none (`ck_action_revisions_columns_written`, `docs/architecture/schema/actions.md`, the `check` line under `action_revisions`).
- A vote revision keeps the result and the question only as fact rows in `vote_revision_facts`, as text "Result: ..." and "Question: ..." (`src/CivicLens.Publication.Contracts/PublishedVote.cs:17`, `docs/architecture/votes.md:49`). Neither is a column of `votes`.
- Every public contract names an official by the configured official ID, the `person_id` of the tables. `PublishedOfficial.Id` holds it (`src/CivicLens.Publication.Contracts/PublishedOfficial.cs:5`). So do `PublicationProtocol.OfficialPagePath` (`PublicationProtocol.cs:48`) and the search filter `official=` (`SearchRequest.cs:43`, `SearchIndex.cs:111`).
- No published contract carries a bioguide ID. `PublishedOfficialProfile` holds the ID, name, Wikidata ID, website, photo, and terms (`PublishedOfficialProfile.cs:5-10`). The PR views show `bioguide_id` and hide `person_id` (`PR:docs/architecture/publication.md:592`). So a page cannot name an official to a view route today. See D12.
- The vote member table has an index on `(person_id, list_id)` (`docs/architecture/schema/votes.md:106`). The sponsorship table has one on `(person_id, record_id, revision_number)` (`docs/architecture/schema/actions.md:122`). Neither table has an index on `bioguide_id`.

## 1. Step 21: serve votes and bills

### 1.1 Common rules

- All new routes read through one new data source that logs in as `civic_lens_public`. It never reads the search database or a base table.
- The role allows 5 connections (`PR:...PublicApiGrants.cs:24`). Set `Maximum Pool Size=4` in the connection string. One service runs for each search database (`docs/architecture/search.md:238`).
- Add the option `CIVIC_LENS_PUBLIC_VIEWS_DATABASE`. The role already sets `statement_timeout` to 2 seconds (`PR:...PublicApiGrants.cs:26`). So the data source needs no timeout option, unlike the search reader (`src/CivicLens.PublicApi/PublicApiOptions.cs:32-38`).
- Each answer holds `release`. The value is `active_records.release_number`. One query reads rows and release together, so they cannot differ.
- Page size: default 20, at most 50. This is `SearchRequest.MaximumLimit = PublicationProtocol.PageSize` (`src/CivicLens.PublicApi/Search/SearchRequest.cs:11-12`, `src/CivicLens.Publication.Contracts/PublicationProtocol.cs:14`).
- Errors use problem JSON from `ApiResults` (`docs/architecture/search.md:141`). A bad parameter gives 400. A release change inside a walk is decision D1.
- The views show the active release only (`PR:docs/architecture/publication.md:589`). So these routes take no `release` pin that differs from the active one.
- Every response keeps `Access-Control-Allow-Origin: *` and no cookie (`src/CivicLens.PublicApi/PublicApiApp.cs:62-66`).

### 1.2 Vote endpoints

| Path | Query parameters | Response | Reads |
|---|---|---|---|
| `GET /v1/votes` | `chamber`, `congress`, `session`, `kind`, `billType`, `billNumber`, `issue`, `from`, `to`, `cursor`, `limit` | `{release, votes: [VoteRow], nextCursor}` | `votes`, and `vote_issues` for `issue` |
| `GET /v1/votes/{recordId}` | none | `{release, vote: VoteRow, issues: [{issueId, isTracked}]}` | `votes`, `vote_issues` |
| `GET /v1/votes/{recordId}/members` | `cast`, `cursor`, `limit` | `{release, members: [{position, officialId, cast}], nextCursor}` | `vote_members` |

`VoteRow` is: `recordId`, `revisionNumber`, `chamber`, `congress`, `session`, `rollCall`, `votedOn`, `kind`, `billType`, `billNumber`, `headline`, `summary`, `textOrigin`, `label`, `question`, `result`, `yea`, `nay`, `present`, `notVoting`.

- Order of `/v1/votes`: `voted_on` desc, `chamber` asc, `session_number` desc, `roll_call_number` desc, `record_id` asc. This is the order of `PublishedOfficialVotes.Of` (`src/CivicLens.Publication.Contracts/PublishedOfficialVotes.cs:16-17`). `PublishedChamber` is `House, Senate`, so text order equals enum order.
- Order of members: `position` asc. The key is `(record_id, revision_number, position)`. A Senate or House list holds at most 600 members (`PublishedVote.MaximumMembers`). A full House vote needs 9 pages.
- `question` and `result` are new view columns (decision D3). `votes` has neither today. `question` is nullable. A vote with no `Question: ` fact gets the kind `Other` (`src/CivicLens.Application/Votes/VoteKindTable.cs:26-27`). `PublishedVote` has `Result` (`PublishedVote.cs:35`). No contract has a `Question` field. So `question` is a new public field.
- The counts are null when a cast is a candidate name (`PR:...PublicApiViews.cs:35-38,46`). The response keeps the null.
- `textOrigin` and `label` travel together. A `Model` text always has its label. A `Person` text has none. The view guarantees this (`PR:docs/architecture/publication.md:591`).
- Members show an official key (decision D12). The page needs names (decision D8).
- The cursor holds the release and the sort key (decision D4).

The keyset query must match its `ORDER BY`. The tuple form cannot mix directions, so use an OR chain. The five cursor values are `$1` to `$5`.

```sql
SELECT * FROM public_api.votes
 WHERE voted_on < $1
    OR (voted_on = $1 AND chamber > $2)
    OR (voted_on = $1 AND chamber = $2 AND session_number < $3)
    OR (voted_on = $1 AND chamber = $2 AND session_number = $3 AND roll_call_number < $4)
    OR (voted_on = $1 AND chamber = $2 AND session_number = $3 AND roll_call_number = $4 AND record_id > $5)
 ORDER BY voted_on DESC, chamber, session_number DESC, roll_call_number DESC, record_id
 LIMIT 51;
```

An OR chain may not use an index well. Section 3.2 measures it. The other fix is to change the order so all keys run the same way. That changes the order that the static pages use.

### 1.3 Bill endpoints

| Path | Query parameters | Response | Reads |
|---|---|---|---|
| `GET /v1/bills` | `congress`, `billType`, `billNumber`, `sponsor` (official ID), `issue`, `policyArea`, `from`, `to`, `cursor`, `limit` | `{release, bills: [BillRow], nextCursor}` | `bills`, `bill_issues`, `bill_sponsorships` |
| `GET /v1/bills/{recordId}` | none | `{release, bill: BillRow, issues: [issueId]}` | `bills`, `bill_issues` |
| `GET /v1/bills/{recordId}/sponsorships` | `role`, `cursor`, `limit` | `{release, sponsorships: [...], nextCursor}` | `bill_sponsorships` |

`BillRow` is: `recordId`, `revisionNumber`, `congress`, `billType`, `billNumber`, `billTitle`, `headline`, `summary`, `textOrigin`, `label`, `policyArea`, `policyAreaId`, `eventDate`, `sponsorCount`, `cosponsorCount`.

- The `sponsor` filter takes the official ID, as the search filter `official=` does (`SearchRequest.cs:43`). It uses `ix_action_revision_sponsorships_person` (`docs/architecture/schema/actions.md:122`). It needs no new index if D12 picks option (a).
- Order of `/v1/bills`: `event_date` desc, then `record_id` asc. `event_date` is the newest sponsored or withdrawn date, as the page and the manifest entry use it (`src/CivicLens.Publication.Contracts/PublishedAction.cs:43`, `docs/architecture/actions.md:33`).
- A page of bills needs the tracked issues of at most 50 records. Read them with one batch query over `bill_issues` for the page record IDs, not one query for each bill (`AGENTS.md`, "Avoid unnecessary repeated reads").
- Sponsorships can reach 600 for a bill (`PublishedAction.MaximumSponsorships`, `PublishedAction.cs:5`). They get their own paged route. The detail answer holds only the counts.

### 1.4 What changes in PublicApi

Today `PublicApi`:

- reads `release.json` and record files over HTTP every 30 seconds (`docs/architecture/search.md:13`);
- writes them to the search database (`PublicApiApp.cs:38,45`);
- answers search and record lookups from that database (`SearchEndpoints.cs:14-30`);
- stores reader reports there (`PublicApiApp.cs:40`).

After step 21:

- Added: the third data source, the option, the vote and bill routes, and one cursor helper. A cache header helper is added only if D10 picks it.
- Kept: ingest, the search database, `/v1/search`, `/v1/records/{id}`, and reports. Full-text search needs body text, and the views carry none (`docs/architecture/search.md:27`).
- Stopped: nothing stops in step 21. The vote and bill routes never touch the release files or the search database.
- Stops later (roadmap, last item of R-68): the release manifest as a data path (`docs/roadmap.md:135`, `docs/architecture/publication.md:683`). That change belongs to the page rebuild PRs, not to step 21.
- Two sources of "active release" now exist: `active_release` in the search database (set by a 30 second poll) and `publication_releases.is_active`. They can differ for up to one poll. See D2.

### 1.5 Proposed bill views

All follow the vote precedent. Each joins `public_api.active_records` with `kind = 'action'`. Each is `security_barrier`. Each is owned by `civic_lens_public_owner` and gets one SELECT grant to `civic_lens_public` (`PR:...PublicApiViews.cs:19-67`, `PR:...PublicApiGrants.cs:30-38`).

**The unmoved-revision filter.** An old revision has only `revision_json` and no columns. The vote views skip it with `member_list_id IS NOT NULL` (`PR:...PublicApiViews.cs:48`). For actions, the check `columns_written` makes all 15 content columns null together. So the filter is `r.headline IS NOT NULL`. A published action has 1 to 600 sponsorships (`PublishedAction.cs:65-66`). So the filter also needs one sponsorship row. All three bill views apply the same filter. Add a test like `AVoteRevisionWithNoWrittenColumnsAppearsInNoVoteViewBecauseItHoldsNothingToShow` (`PR:docs/architecture/publication.md:644`). Add a second test that the three bill views list the same record set.

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
              count(DISTINCT p.person_id) FILTER (
                WHERE p.role = 'Cosponsor' AND p.withdrawn_on IS NULL
                  AND NOT EXISTS (SELECT 1 FROM public.action_revision_sponsorships x
                                   WHERE x.record_id = p.record_id AND x.revision_number = p.revision_number
                                     AND x.role = 'Sponsor' AND x.person_id = p.person_id)) AS cosponsor_count
         FROM public.action_revision_sponsorships p
        WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number) s
 WHERE a.kind = 'action' AND r.headline IS NOT NULL AND s.event_date IS NOT NULL;
```

The view needs these base columns, so the owner grants must cover all of them:

- On `action_revisions`: `record_id`, `revision_number`, `congress`, `bill_type`, `bill_number`, `bill_title`, `headline`, `summary`, `text_origin`, `policy_area`, `policy_area_id`.
- On `action_revision_sponsorships`: `record_id`, `revision_number`, `position`, `person_id`, `role`, `is_original`, `sponsored_on`, `withdrawn_on`.
- On `action_revision_issues`: `record_id`, `revision_number`, `issue_id`, `position`, `is_tracked`.

Notes on this view:

- The `event_date` formula copies `PublishedSponsorship.LatestDate` (`src/CivicLens.Publication.Contracts/PublishedSponsorship.cs:12`). The date has two C# owners already: `ActionDraftRevision.EventOn` (`src/CivicLens.Core/Actions/ActionDraftRevision.cs:42`) and `LatestDate`. The SQL is a third copy. The test must compare it with `PublishedAction.ToEntry().EventDate`, as the vote test compares `VoteTally`.
- The cosponsor count follows `CosponsorIdsOfEntry` (`PublishedAction.cs:47-53`). It drops a cosponsor that is also a sponsor, and it counts distinct official IDs, not bioguide IDs. The test must show equal counts. See D6.
- `sponsor_count` has no C# owner. The manifest entry holds `CosponsorIds` only. The count is plain row count of role `Sponsor`.
- `ORDER BY event_date` sorts on a lateral aggregate. No index can serve that order. Every active bill gets its aggregate before the sort and the `LIMIT`. This is one more reason to look at D6 option (b).
- The wording CASE repeats `PublishedRecordLabels`. Add a test as the vote one does (`PR:docs/architecture/publication.md:309`).

**`public_api.bill_sponsorships`**

```sql
CREATE VIEW public_api.bill_sponsorships WITH (security_barrier = true) AS
SELECT a.record_id, a.revision_number, p."position", p.person_id AS official_id, p.role,
       p.is_original, p.sponsored_on, p.withdrawn_on
  FROM public_api.active_records a
  JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
  JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number
 WHERE a.kind = 'action' AND r.headline IS NOT NULL;
```

Every revision that has `headline` also has a sponsorship row here, because the join to `p` drops any other. The `bills` view drops a revision with no sponsorship through `event_date IS NOT NULL`. A row of this view for a revision with no sponsorship cannot exist, so both views list the same records. The test above checks it.

**`public_api.bill_issues`**

```sql
CREATE VIEW public_api.bill_issues WITH (security_barrier = true) AS
SELECT a.record_id, a.revision_number, i.issue_id, i."position"
  FROM public_api.active_records a
  JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
  JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number
 WHERE a.kind = 'action' AND r.headline IS NOT NULL AND i.is_tracked
   AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships x
                WHERE x.record_id = r.record_id AND x.revision_number = r.revision_number);
```

The page publishes tracked issues only (`PublishedAction.TrackedIssueIds`, `PublishedAction.cs:22`). The view keeps `position`, so the route can keep the stored issue order. See D7.

**Public fields**: bill identity, title, headline, summary, `text_origin`, `label`, policy area name and ID, sponsor and cosponsor dates, official ID, role, `is_original`, the two counts, the event date.

**Never public**:

- `suggestion_headline`, `suggestion_summary`, `suggestion_run_id`, `suggestion_fingerprint`. The suggestion columns are model drafts, not approved text.
- Notes: review decision notes and correction notes. No view joins them.
- `author_subject`, `author_kind`, `run_id`, `candidate_*`, `fingerprint`, `subjects_hash`, `issue_revision_id`, `issue_candidates_recorded`, `source_id`, `summary_version`.
- `revision_json`, `created_at_utc_ticks`.
- All `*_citation_*` columns. The vote views hide them too (`PR:docs/architecture/publication.md:592`). Evidence display is planned work.

The owner role then gets column grants for exactly the columns above (`PR:...PublicApiGrants.cs:30-35` is the model). Add each view column to `public-api-columns.txt` in the same change.

## 2. Step 22: the official page with a vote strip

### 2.1 What the strip shows

One row for each recent vote of one official: date, the question, the result, the cast, the headline, and a link to the vote page. The official's party and the party majority drive the "voted with most of their party" text and the filter "Votes against their party" (`src/CivicLens.Host/Publication/PublishedOfficialView.cs:174,225`). The static site builds all this from `PublishedOfficialVotes` (`src/CivicLens.Publication.Contracts/PublishedOfficialVote.cs`, `PublishedOfficialVotes.cs:6-17`), with up to 5,000 rows for each Congress.

### 2.2 View and endpoint

The route key is the official ID, the key of every public contract (decision D12). The view `public_api.official_votes` joins the two vote views, so the question, the result, and the totals keep one owner.

```sql
CREATE VIEW public_api.official_votes WITH (security_barrier = true) AS
SELECT m.official_id, v.record_id, v.revision_number, v.release_number,
       v.voted_on, v.chamber, v.congress, v.session_number, v.roll_call_number,
       v.kind, v.bill_type, v.bill_number, v.headline, v.question, v.result,
       v.yea, v.nay, v.present, v.not_voting, m.vote_cast
  FROM public_api.votes v
  JOIN public_api.vote_members m
    ON m.record_id = v.record_id AND m.revision_number = v.revision_number;
```

This body uses `official_id` in `vote_members` (D12 option a). The `votes` view carries `question`, `result`, and the totals. Section 3.2 shows how it computes them. The plan check is whether a filter on `official_id` reaches the `person_id` index through two `security_barrier` views. If it does not, repeat the lateral bodies of `votes` inside `official_votes`.

- The view carries the totals. The strip needs `Totals` for the split vote text.
- The party, `PartyMajority`, and `SplitVote` fields of `PublishedOfficialVote` are not in this view yet. No table stores a member party. The C# code reads the party from the roster terms on the vote date (`src/CivicLens.Application/Publication/VoteTally.cs:35`, `VoteParties.PartyOn`). The views cannot read roster tables today. This is part of D8. The view needs three more columns: `party`, `party_majority`, `split_vote`.
- It joins on `person_id`, so the existing index serves it (`docs/architecture/schema/votes.md:106`).
- `GET /v1/officials/{officialId}/votes?congress=&kind=&cursor=&limit=` returns `{release, officialId, votes: [{recordId, votedOn, chamber, congress, session, rollCall, billType, billNumber, headline, kind, question, result, cast, side, party, partyMajority, splitVote, totals}], nextCursor}`.
- Order: the same as `/v1/votes`. The cursor form is decision D4.
- The route returns the raw cast and a `side` field (decision D9).

### 2.3 Paging and caching rules

- The server-rendered official page holds the first 10 votes. The release renders it once. The static page is a file, so it has the file header `public, max-age=60` (`docs/operations.md:993`).
- The page embeds its release number in a data attribute. The script calls `/v1/officials/{id}/votes?release=<N>&limit=50`.
- The API answers 200 only when `N` equals the active release. Otherwise it answers 409 with the active release number. The script then shows "This page has a newer version" with a reload link. See D1.
- Whether any layer caches that JSON is decision D10. Nothing documented caches it today.
- Each later page uses the cursor from the answer. The cursor holds the release, so a walk cannot mix two releases.
- A reader without script sees the first 10 votes and a link to the Congress vote page. The facet and static pages stay the path for those readers (`docs/architecture/search.md:29`).
- Worst case for one official: 5,000 votes in a Congress is 100 pages of 50. The strip loads a page only on a click.
- A new insight follows the same recipe: one view, one route, one shared component (`docs/roadmap.md:135`).

## 3. Load on the shared database

The role reads the operational database that the worker writes. Reads use MVCC and do not block the worker. The risks are CPU, I/O, buffer cache, the 5 connection limit, and any DDL lock from a migration.

**Run these on a restored copy or a staging database. Do not run them on production.** Each step names the role that can run it. Only the view queries run as `civic_lens_public`. The role holds no grant on a base table, no right to create an index, and no right to read `pg_stat_statements` of others.

- The role is created `NOLOGIN`. On the copy, enable the login by hand as `docs/operations.md` describes (`PR:docs/operations.md:282`). Use a throwaway password.
- The owner role below means the database owner of the copy, the app role. It is a superuser on the compose database (`PR:docs/architecture/publication.md:306`).
- The 2 second statement limit applies to the role. A plan that hits it fails. Record that as a result.

### 3.1 Size and shape of the data

Role: owner. The role `civic_lens_public` cannot read these tables.

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

Role: `civic_lens_public`. Use `EXPLAIN (ANALYZE, BUFFERS, SETTINGS)`. Replace the literal IDs with real ones from the copy.

The new `votes` columns come from two lateral lookups. The first fact with the prefix, by position, wins, as `VoteFacts.Find` does (`src/CivicLens.Application/Votes/VoteFacts.cs:8-9`). The `votes` view adds these joins and the columns `q.question` and `r.result`:

```sql
  LEFT JOIN LATERAL (
       SELECT substr(f.fact_text, length('Question: ') + 1) AS question
         FROM public.vote_revision_facts f
        WHERE f.record_id = v.record_id AND f.revision_number = v.revision_number
          AND starts_with(f.fact_text, 'Question: ')
        ORDER BY f."position" LIMIT 1) q ON true
  LEFT JOIN LATERAL (
       SELECT substr(f.fact_text, length('Result: ') + 1) AS result
         FROM public.vote_revision_facts f
        WHERE f.record_id = v.record_id AND f.revision_number = v.revision_number
          AND starts_with(f.fact_text, 'Result: ')
        ORDER BY f."position" LIMIT 1) r ON true
```

The prefixes are `VoteFactPrefixes.Question` and `VoteFactPrefixes.Result`. The grant needs `record_id`, `revision_number`, `position`, `fact_text` on `vote_revision_facts`.

```sql
-- The active release view alone. Watch the scan type of publication_record_states.
EXPLAIN (ANALYZE, BUFFERS) SELECT count(*) FROM public_api.active_records;
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM public_api.active_records WHERE kind = 'vote' LIMIT 50;

-- First page of votes. Check that LIMIT stops the lateral count early.
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.votes
 ORDER BY voted_on DESC, chamber, session_number DESC, roll_call_number DESC, record_id
 LIMIT 51;

-- Keyset page. The five values come from the last row of the first page. The OR chain matches the ORDER BY.
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.votes
 WHERE voted_on < DATE '2026-03-01'
    OR (voted_on = DATE '2026-03-01' AND chamber > 'House')
    OR (voted_on = DATE '2026-03-01' AND chamber = 'House' AND session_number < 1)
    OR (voted_on = DATE '2026-03-01' AND chamber = 'House' AND session_number = 1 AND roll_call_number < 120)
    OR (voted_on = DATE '2026-03-01' AND chamber = 'House' AND session_number = 1 AND roll_call_number = 120 AND record_id > '<record id>')
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

-- The vote strip of one official, after official_votes exists. It should use the person_id index.
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.official_votes WHERE official_id = '<official id>'
 ORDER BY voted_on DESC, chamber, session_number DESC, roll_call_number DESC, record_id LIMIT 51;

-- Bills, after the bill views exist.
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM public_api.bills ORDER BY event_date DESC, record_id LIMIT 51;
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM public_api.bills WHERE congress = 119 AND bill_type = 'hr' AND bill_number = 1;
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM public_api.bill_sponsorships WHERE record_id = '<action record id>' ORDER BY "position" LIMIT 51;
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM public_api.bill_issues WHERE (record_id, revision_number) IN (('<id1>',1),('<id2>',1) /* up to 50 pairs */);
```

What to read in each plan:

- Does `Limit` sit above the lateral subquery? If a full sort runs before it, the count of every vote runs (about 440 member rows for each vote). For `bills`, the sort always follows the aggregate, as section 1.5 says.
- Does the OR chain use an index, or does it scan all active votes?
- Is a filter pushed below the view? A `security_barrier` view blocks the push of any qual whose operator is not leakproof. Equality and range on integers, dates, and text normally pass.
- `Rows Removed by Filter`, `Heap Fetches`, and `Buffers: shared read` against `shared hit`.
- A rollback test: activate an older release and run the `active_records` plan again. Open ranges are not a fixed set then.

### 3.3 Indexes to test

Role: the owner of each table. `civic_lens_public` cannot create an index. Run `CREATE INDEX` on the copy, not through the views.

```sql
-- Ordering and filters of the vote list. vote_revisions has no index on voted_on.
CREATE INDEX CONCURRENTLY ix_vote_revisions_voted_on
    ON vote_revisions (voted_on DESC, chamber, session_number DESC, roll_call_number DESC, record_id);

-- Bill lookups by identity.
CREATE INDEX CONCURRENTLY ix_action_revisions_bill
    ON action_revisions (congress, bill_type, bill_number);
```

Test these two only if the plans in 3.2 show a scan. The `official` and `sponsor` lookups need no new index if D12 picks option (a). Under option (b) they need `(bioguide_id, list_id)` on `vote_member_list_entries` and `(bioguide_id, record_id, revision_number)` on `action_revision_sponsorships`.

- The first fact lookup needs no index. The key of `vote_revision_facts` is `(record_id, revision_number, position)`, and a revision holds at most 64 facts (`PublishedVote.MaximumFacts`). The key already serves the lateral.
- Existing: `ix_vote_member_list_entries_person (person_id, list_id)`, `IX_vote_revisions_member_list_id`, `ix_action_revision_sponsorships_person`, `IX_publication_record_states_first_release` and `_last_release` (`docs/architecture/schema/votes.md:106,193`, `schema/actions.md:122`, `schema/publication.md:12-13`).
- Judge each new index with the plans above and with `pg_relation_size`. Each index adds cost to every worker write to that table.
- `CREATE INDEX CONCURRENTLY` cannot run inside a migration transaction. See D11. This is a schema decision for the owner (`docs/agent-rules.md:16`).

### 3.4 Load and effect on the worker

Role: the owner, except the load script. `pg_stat_statements` needs the extension on the copy, and a role that is a superuser or a member of `pg_read_all_stats` sees the query text of other roles. `civic_lens_public` sees none of it. The `pg_stat_activity` and `pg_locks` queries also need the owner to show the worker rows.

```sql
-- Statements of the role, if pg_stat_statements is enabled. Role: owner or pg_read_all_stats.
SELECT left(query, 80) AS query, calls, round(mean_exec_time::numeric, 2) AS mean_ms,
       round(max_exec_time::numeric, 2) AS max_ms, shared_blks_hit, shared_blks_read
  FROM pg_stat_statements s JOIN pg_roles r ON r.oid = s.userid
 WHERE r.rolname = 'civic_lens_public'
 ORDER BY total_exec_time DESC LIMIT 20;

-- Connection use and waits while the load runs. Role: owner.
SELECT usename, state, wait_event_type, wait_event, count(*)
  FROM pg_stat_activity WHERE datname = current_database() GROUP BY 1,2,3,4 ORDER BY 5 DESC;

-- Lock waits of the worker during the test. Role: owner.
SELECT l.pid, l.mode, l.granted, a.query
  FROM pg_locks l JOIN pg_stat_activity a USING (pid) WHERE NOT l.granted;

-- Cache and I/O. Role: owner.
SELECT relname, heap_blks_read, heap_blks_hit, idx_blks_read, idx_blks_hit
  FROM pg_statio_user_tables WHERE relname IN ('vote_revisions','vote_member_list_entries','publication_record_states');
```

Load script, for a copy only. The `pgbench` clients log in as `civic_lens_public`, with 4 clients, the pool size, one file for each query of 3.2:

```sh
pgbench -n -c 4 -j 2 -T 60 -P 10 -f votes_page.sql  "$COPY_URL_AS_PUBLIC_ROLE"
pgbench -n -c 4 -j 2 -T 60 -P 10 -f official_strip.sql "$COPY_URL_AS_PUBLIC_ROLE"
```

Run each with the worker idle, then with a publish and an analysis stage running. Compare the p95 of the worker queries before and after. The worker queries need the owner role. Record the result in the Limits section of `docs/architecture/publication.md`, not in a report file (`AGENTS.md`, documentation rules).

Capacity estimate: the limit is 4 pool connections. If a query takes 20 ms, one connection serves 50 queries a second, so the pool serves about 200 a second. No documented layer caches API JSON today (D10). So the origin sees every request, up to the rate limit of each client (`PublicApiOptions.cs:24`, 60 a minute by default).

## 4. PR order

Each PR starts from `origin/main` (`docs/agent-rules.md:7`). A PR that needs an earlier one waits for its merge. The owner merges.

| PR | Content | Needs |
|---|---|---|
| 0 | The open `public-api-views` PR: schema, role, four views. If D12 picks option (a), it also swaps `bioguide_id` for `official_id`. | none |
| 1 | Vote view columns `question` and `result`, grants for `vote_revision_facts`, columns file, tests. | 0 |
| 2 | Bill views `bills`, `bill_sponsorships`, `bill_issues`, grants, columns file, label, event date and cosponsor tests, the unmoved-revision test. | 1 (same grants and columns files) |
| 3 | `PublicApi` plumbing with its first user: the views database option, pool limit, cursor helper, `GET /v1/votes` and `GET /v1/votes/{id}`. Docs and architecture test update. | 1 |
| 4 | `GET /v1/votes/{id}/members`. | 3 |
| 5 | `GET /v1/bills`, `/v1/bills/{id}`, `/v1/bills/{id}/sponsorships`. | 2, 3 |
| 6 | Measurement on a copy with the SQL of section 3. Index decisions. Results into `publication.md` Limits. Index migration if the plans need it. | 3, 4, 5 |
| 7 | View `official_votes` and `GET /v1/officials/{officialId}/votes` with the release parameter. Cache headers only if D10 picks them. The party columns of D8. | 6 |
| 8 | Shared vote strip component in `public.js`, and the server-rendered strip on the official page. Browser check at 360 and 1280 px, light and dark (`docs/agent-rules.md:34`). | 7 |

Plumbing is in PR 3 and not alone, because an added member needs a production caller (`AGENTS.md`, verification).

Every PR updates `docs/roadmap.md` (completed work leaves the list) and the feature doc in place. A migration PR regenerates the schema docs (`docs/agent-rules.md:33`).

## 5. Open decisions for the owner

### D1. A release changes while a reader walks pages

- Problem: the views show the active release only, and there is no release parameter. A reader could get page 1 from release 12 and page 2 from release 13.
- Precedent checked: search serves the release of an older cursor, pinned (`SearchRequest.cs:49`, `Release = release ?? cursor?.Release`). It refuses only a cursor that conflicts with an explicit `release` parameter, with 400 (`SearchRequest.cs:36-37`). An unknown release gives 404 (`docs/architecture/search.md:67`). No route answers 409. So option (a) is new. Search today is closest to option (c).
- Options:
  - (a) The cursor holds the release. The route answers 409 with the active number when it differs. This is a new status for this API. Cost: a reader restarts the walk after a publish. Benefit: no mixed pages.
  - (b) Ignore the change. Cost: a row can repeat or vanish between pages. Benefit: nothing to build.
  - (c) Make the view take a release number, so a cursor stays pinned as in search. Cost: a function or a parameter table, which the PR rules out (`PR:docs/architecture/publication.md:589`). Benefit: pinned reads like search.
- Recommendation: (a).

### D2. Which source names the active release

- Problem: `/v1/release` reads the search database. The new routes read the views. They can differ for up to 30 seconds.
- Precedent checked: the search service follows `release.json` by polling (`docs/architecture/search.md:13,17`). The view follows `is_active` at once (`PR:docs/architecture/publication.md:308`).
- Options:
  - (a) Keep both. Cost: a page can show two numbers.
  - (b) Move `/v1/release` to the views in PR 3. Cost: `ReleaseInfo` has `PublishedAtUtc` (`src/CivicLens.PublicApi/Search/ReleaseInfo.cs:3`). So the view `active_records` needs a new column `published_at_utc_ticks`, a column grant on `publication_releases`, and a line in `public-api-columns.txt`. Search results keep their own release field.
  - (c) Add `releaseSource` to answers. Cost: more wording for readers.
- Recommendation: (b). The views are the single truth for R-68, and search still pins its index release.

### D3. Where `result` and `question` come from

- Problem: both are fact rows, not columns. The strip needs them. `question` can be absent, so it is nullable.
- Precedent checked: the vote kind is stored at draft time and publication never derives it again (`docs/architecture/votes.md:49`). The label CASE repeats wording in SQL with a test (`PR:...PublicApiViews.cs:30-34`). `VoteFacts.Find` takes the first fact by position (`VoteFacts.cs:8-9`).
- Options:
  - (a) Read the fact rows in the view with the two laterals of section 3.2. The lateral takes the first match by position. Tests: `result` against `PublishedVote.Result`, and `question` against `VoteFacts.Find` on the stored facts, because no contract has a question. Cost: a second owner of each prefix. No extra index, because the key `(record_id, revision_number, position)` serves it.
  - (b) Store `result` and `question` as columns in the vote revision, written by the draft step. Cost: contract, draft shape, and backfill. This needs your decision (`docs/agent-rules.md:16`). Benefit: one owner and the cheapest read.
  - (c) Leave them out of the strip. Cost: the strip loses two of its asked fields.
- Recommendation: (a) now, with the tests. Move to (b) only if the plans in 3.2 show a cost.

### D4. Cursor form

- Problem: a keyset needs the sort key. A cursor can carry it or can carry the record ID only.
- Precedent checked: search has three cursor forms (`SearchCursor.cs:30-36`). The ranked form holds the rank bits. The dated form holds the sort date and the publish ticks. The sorted form holds the record ID only, and the query reads the key of that record in the same release (`docs/architecture/search.md:24`). The default date order of search uses the dated form (`SearchIndex.cs:143`). The vote and bill lists are date ordered, so the closest precedent is the dated form, option (b). Review pages carry a time and an ID (`docs/architecture/review.md:214`).
- Options:
  - (a) Record ID only, as the sorted form. Cost: one extra key lookup in each page query. A forged or retired record ID gives an empty page. Benefit: one short format.
  - (b) The full key in the cursor, as the dated form: release, `voted_on`, chamber, session, roll call, record ID. Cost: a longer cursor and a client can forge any key. A forged key only moves the window over public rows. Benefit: no lookup, and a page survives a record that leaves the release.
- Recommendation: (b).

### D5. Bill identity and grain

- Problem: bills have a record ID and also a `(congress, type, number)` identity. The path must pick one.
- Precedent checked: record pages use `records/{recordId}.html` (`PublicationProtocol.RecordPagePath`, `PublicationProtocol.cs:59`). The search route uses the record ID (`SearchEndpoints.cs:22`). The index `ux_action_drafts_bill` is unique on `(source_id, congress, bill_type, bill_number)` (`docs/architecture/schema/actions.md:67`). So one draft holds one bill for each source. The routes show no source, so the triple is unique only while one source feeds actions.
- Options:
  - (a) Path by record ID. Filter by the bill triple.
  - (b) Path by `/v1/bills/{congress}/{type}/{number}`. Cost: a unique rule the view must keep when a second source arrives.
  - (c) Both.
- Recommendation: (a).

### D6. Computed counts in the bill view

- Problem: the event date and the cosponsor count are computed per row, as the vote counts are.
- Precedent checked: vote counts use a lateral count and one test against `VoteTally` (`PR:...PublicApiViews.cs:41-47`). A stored date has a precedent: `action_drafts.event_on` and `vote_drafts.event_on` are columns (`docs/architecture/schema/actions.md:52`, `schema/votes.md:77`). They hold the current revision and are not published data. The event date already has two C# owners: `ActionDraftRevision.EventOn` (`ActionDraftRevision.cs:42`) and `PublishedSponsorship.LatestDate` (`PublishedSponsorship.cs:12`). `CosponsorIdsOfEntry` removes sponsors and dedups by official ID, not bioguide ID (`PublishedAction.cs:47-53`).
- Options:
  - (a) Lateral aggregate in the view. Cost: a third copy of the date rule and a copy of the cosponsor rules, and a test for each. Sorting by `event_date` cannot use an index.
  - (b) Store both as columns on `action_revisions` when the revision is written, with an index on the date. Cost: schema change and backfill, and the writer must keep one owner of the date rule.
- Recommendation: (a), because it follows the vote view. Take (b) if the plans show a slow sort or a slow lateral on 600-sponsorship bills.

### D7. Untracked issues in `vote_issues`

- Problem: `vote_issues` shows `is_tracked`, so it carries untracked issues too. The published page and `PublishedOfficialVote.IssueIds` hold tracked ones only (`PublishedOfficialVote.cs:26`, `PublishedAction.cs:22`).
- Precedent checked: the PR view exposes the flag (`PR:...PublicApiViews.cs:57-62`).
- Options:
  - (a) Keep the flag. The route filters. Cost: a route bug could show an untracked issue.
  - (b) Filter in the view, as proposed for `bill_issues`. Cost: changes `public-api-columns.txt` and the PR's view.
- Recommendation: (b) for the bill view in PR 2. Ask whether to change `vote_issues` the same way. What a view never shows cannot leak.

### D8. Names and party of members

- Problem: `vote_members` has an official key only. A vote page needs names. The strip needs the party and the party majority on the vote date. The roster lives in other tables and in `officials.json`, and `VoteParties.PartyOn` reads the roster terms.
- Precedent checked: the PR hides person IDs and citations (`PR:docs/architecture/publication.md:592`). R-68 says no JSON file carries data to the public path (`docs/roadmap.md:135`). The vote kind is stored at draft time, not derived later (`docs/architecture/votes.md:49`).
- Options:
  - (a) The page joins names from the release at render time. Cost: keeps a JSON data path for now. It does not give the party to the strip route.
  - (b) A new view `officials` over the roster tables, keyed by official ID, plus `party`, `party_majority`, and `split_vote` columns in `official_votes` that read the roster terms. Cost: a new view, grants on roster tables, and a SQL copy of the party-on-date rule with a test.
  - (c) Store the party of each member on the vote member row when the list is written. Cost: schema change and backfill. Benefit: one owner and no roster join.
- Recommendation: (b) for names, as a later PR. For the party, prefer (c) if the owner accepts the schema change, because it follows the stored vote kind. Step 21 returns the official key only.

### D9. Where the cast to side rule lives

- Problem: "Yea or Aye is yea" lives in C# (`PublishedOfficialVote.SideOf`, `PublishedOfficialVote.cs:31-36`) and in SQL (`PR:...PublicApiViews.cs:42-46`). A browser component would add a third copy.
- Precedent checked: the PR already accepts a tested SQL copy for counts (`PR:docs/architecture/publication.md:310`).
- Options:
  - (a) The route returns the cast and a `side` column from the view (`yea`, `nay`, or null). The component shows the field. One more tested SQL copy, no browser rule.
  - (b) The component maps the cast. A third copy with no SQL test.
- Recommendation: (a). Add `side` to `official_votes` and test it against `SideOf`.

### D10. Caching of API answers

- Problem: the first page of a strip has no release in its URL, and the claim that an edge caches the JSON is not backed. Cloudflare caches JSON only when a Cache Rule says so. The docs cover static files only (`docs/operations.md:993`). No rule for `/v1` exists in the docs.
- Precedent checked: files have `public, max-age=60` and the operator purges by hand (`docs/operations.md:786,993`). The search service reads Caddy on loopback, not through the CDN (same line 993).
- Options:
  - (a) The page embeds its release and the script sends `release=N`. The API answers 409 when N is not active. The API sends `Cache-Control: public, max-age=60` on a 200 and `no-store` on 409 and 5xx. The owner adds a Cloudflare Cache Rule for `/v1`. Cost: one extra branch for readers of an old page, and an edge rule that no doc describes yet.
  - (b) No cache. The origin serves every request. Cost: the pool and the rate limit carry all load (section 3.4). Benefit: nothing to configure.
  - (c) Purge by tag at each publish. Cost: a Cloudflare credential and a new deploy step. This breaks the rule that cloud agents hold no secrets for production (`docs/agent-rules.md:43`) only if an agent touches it, but needs owner work.
- Recommendation: (b) for step 21 and 22, so the measurements in section 3 stand on the real load. Pick (a) when the measurements show a need, and document the Cache Rule then.

### D11. Index migrations

- Problem: the new indexes must not block worker writes on large tables.
- Precedent checked: migrations run in `db migrate` with a 10 minute limit (`docs/operations.md` migrate paragraph, `PostgresCollectionAttemptStore.cs` `MigrateAsync`). No migration in the repository uses `CONCURRENTLY` or `suppressTransaction`. So option (b) is a new pattern.
- Options:
  - (a) Plain `CREATE INDEX` in a transaction. Blocks writes during the build. Simple.
  - (b) `CONCURRENTLY` with `suppressTransaction: true`. No write block. A failed build leaves an invalid index to drop by hand. New pattern.
  - (c) Build the index by hand before the migration. Drift between environments.
- Recommendation: (a) if PR 6 shows small tables, because it has a precedent. Choose (b) only if the build time would block the worker. Measure first.

### D12. The key that names an official

- Problem: public contracts key officials by the configured official ID (`PublishedOfficial.Id`, `OfficialPagePath`, the search `official=` filter). No published contract carries a bioguide ID. The PR views show `bioguide_id` and hide `person_id`. A page for `officials/{id}.html` has no way to call a bioguide route. The sponsor filter and the member list have the same gap.
- Precedent checked: `PublishedOfficialProfile` holds no bioguide ID (`PublishedOfficialProfile.cs:5-10`). The official ID appears in every release file already, so exposing it adds no new fact. The PR chose bioguide to hide internal IDs (`PR:docs/architecture/publication.md:592`).
- Options:
  - (a) Expose the official ID in the views. Add `person_id AS official_id` to `vote_members` and `bill_sponsorships`, with column grants on `vote_member_list_entries.person_id` and `action_revision_sponsorships.person_id`. Drop `bioguide_id` from the views, or keep it for later use. Cost: changes the PR 0 view, its grant list, its columns file, and one doc line. Benefit: one key for every contract and route, and the existing person indexes serve the lookups.
  - (b) Add the bioguide ID to the officials file or the official page contract (`PublishedOfficialProfile.BioguideId`). Cost: a public contract change with validation, an older search service that must accept it, and a second public key for each official. New indexes on `bioguide_id` in both tables. Benefit: the views stay as the PR has them, and the key is a public standard.
  - (c) A view `officials` that maps official ID to bioguide ID. Cost: a new view, roster grants, and a join in every official route. It overlaps with D8 option (b).
- Recommendation: (a). It needs no contract change and no index, and the route key then equals the page key. Drop `bioguide_id` from the views unless a reader needs it.
