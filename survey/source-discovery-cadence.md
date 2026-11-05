# Survey: automatic source discovery and measured check cadence (R-27)

This survey covers two parts of roadmap item R-27. Part A is the automatic discovery of official sources for a tracked official. Part B is the check cadence from measured change rates. The move of people, sources, and coverage to tables has its own survey, `survey/officials-in-tables.md` on the branch `survey-officials-in-tables`. That branch was not on origin when this survey was written. This survey therefore assumes that people, sources, and coverage become tables, and that each change makes a new snapshot or revision ID that jobs bind to, as configuration revisions do today.

The survey changes no product code. Each section marks what was measured and what is assumed. "Unverified" means that the sandbox could not reach the source or that the claim comes from memory.

## 0. Sources read and network limits

Read in the repo: AGENTS.md, README.md, docs/architecture.md, docs/agent-rules.md, docs/roadmap.md (R-27), and the feature docs collection.md, configuration.md, roster.md, statements.md, and schema/collection.md. Code read: `PostgresCollectionJobStore.Scheduling.cs`, `WatchedSourceConfiguration.cs`, `JobPreparation.cs`, `CollectionConfiguration.ValidateOfficeOf`, `StatementSpeakerRoster.cs`, `HtmlParser.cs`, `DiscoveryResult.cs`, and `MemberRosterParser.cs`.

Network results, with the User-Agent `CivicLens/0.1 (civic-lens.info)`:

| Host | Result |
|---|---|
| raw.githubusercontent.com | Reached. Read the congress-legislators README and `legislators-current.yaml`, and the api.congress.gov README and `Documentation/MemberEndpoint.md` |
| unitedstates.github.io, api.congress.gov, www.house.gov, www.senate.gov, member sites | Proxy refused (403). Not read |
| ilpubs.stanford.edu, oak.cs.ucla.edu, dl.acm.org, arxiv.org | Not reachable. The original Cho and Garcia-Molina papers were not read |
| storage.googleapis.com | Reached through the fetch tool. Read Grimes and O'Brien, "Microscale Evolution of Web Pages", WWW 2008, a secondary source that quotes Cho and Garcia-Molina section 4.2 |

No browser or TLS spoofing was used. To read the original papers, the owner must allow ilpubs.stanford.edu or oak.cs.ucla.edu in the session network settings, or read them by hand. collection.md says: "Read the original work before choosing the refresh policy." That step is still open. It is a manual step.

## 1. Member press sources today

All counts in this section are measured from `config/federal-coverage.json` on origin/main at b4084ff1.

### 1.1 Counts

| Item | Count |
|---|---|
| People | 542 (539 members with a bioguide ID, plus the President, the Vice President, and Joe Biden) |
| Sources | 1,403: 1,392 member sources, 2 White House feeds, 9 `api` sources |
| Member sources with `officeOf` | 1,392 (all of them, each with its one covered member) |
| Distinct member hosts | 538, one host for each member with sources |
| Members with a bioguide ID and no source | 2: `mitch-mcconnell` (robots disallows all), `jd-vance` (now Vice President) |

Note: configuration.md says "eight `api` sources". The file has nine, because `census-zcta-districts` is also an `api` source. That doc line needs a fix in a later docs PR.

### 1.2 By host pattern and mode

| Host pattern | page | html (listing) | feed | Total |
|---|---|---|---|---|
| `*.house.gov` | 720 | 349 | 69 | 1,138 |
| `*.senate.gov` | 159 | 62 | 33 | 254 |
| Total | 879 | 411 | 102 | 1,392 |

Each member has one to three sources: 346 have three, 162 have two, 30 have one. The source ID suffix shows the role: `about` 478, `press` 411, `issues` 401, `feed` 102. Of 538 members with sources, 406 have an HTML listing and pages, 86 have a feed and pages, 25 have pages only, 16 have a feed only, and 5 have a listing only.

### 1.3 Paths

- House listings: `/media/press-releases` 224, `/news/documentquery.aspx` 65, `/media-center/press-releases` 43, others 17.
- Senate listings: 8 path forms, led by `/news/press-releases` (21 with or without the slash) and `/newsroom/press-releases` (20).
- Feeds: WordPress `/feed` or `/feed/` 77, `?a=rss.feed` 22 (House), `?a=RSS.Feed` 2, `/rss/press-releases` 1.
- Persistent pages: last path segment `issues` 368, `about` 359, `biography` 63, `priorities` 20, and 30 other forms such as `about-mike`.

### 1.4 Profiles, cadence, budgets

- Profiles: 5 (`house-main`, `senate-sitelayout`, `senate-main-content`, `senate-main-tag`, `wp-elementor-single`). `house-main` is on 927 sources. 282 sources have no profile (212 pages, 47 listings, 23 feeds). This is decision D-12.
- Cadence: all 879 pages set `checkIntervalSeconds` 21600 (6 hours). All 513 listings and feeds use the default of 3600 (1 hour). No source has a measured interval.
- Each member source: `minDelayMilliseconds` 1500, `maxRequests` 5, `maxBytes` 2,000,000, `timeoutSeconds` 60, `maxAttempts` 2. Listings and feeds: `maxCandidates` 100 and `admissionPolicy.maxJobs` 2.
- Pacing groups: `house.gov` and `senate.gov`, each 3,000 ms.
- Coverage starts on 2026-10-10 for 1,384 sources and 2026-10-13 for 8. No coverage has an end.

### 1.5 How the sources were first made

Measured from Git: commit 436eaf63 on 2026-10-10 added the whole file in one change, with 533 members and 1,384 sources. The repo holds no generator script. The commit message does not say how the URLs were found. The method is therefore unverified. Later commits fixed single sources (for example the Graham press scope, 30d0b515) and added `officeOf` to every member source (39034e02, 2026-10-29). roster.md says: "Press sources of a new member are a manual step." `roster propose` prints that note for each join.

## 2. Discovery inputs

### 2.1 Official website URL

| Input | Owner | Status | Terms |
|---|---|---|---|
| congress-legislators `terms[-1].url` | unitedstates project (volunteer, not a government body) | Captured daily today as `congress-members`. Stored in `roster_members.url` (varchar 512) | CC0 public domain dedication (measured: README "Public domain" section) |
| Congress.gov member `officialWebsiteUrl` | Library of Congress | Not collected. Needs the api.data.gov key that `congress-bills` already uses | Rate limit 5,000 requests an hour (measured: api.congress.gov README). Data terms unverified |

Measured: in `legislators-current.yaml`, all 539 current members have a `url`. 439 are on `*.house.gov` and 100 are on `*.senate.gov`. For each of the 1,392 configured member sources, the URL host equals the host of the member `url`, after removing `www.`. No source differs. The host rule in section 3 therefore holds for every hand-made source.

Unverified: the field name. `MemberEndpoint.md` names the element `<officialUrl>` in its item level list. The task names `officialWebsiteUrl`. The JSON name must be checked on a live response.

Unverified: the `fromDateTime` filter on `/v3/member`. collection.md says to use it. `MemberEndpoint.md` says "member items at the list level cannot be filtered". The OpenAPI page is on api.congress.gov, which the proxy refused. A live check is needed before PR 8.

### 2.2 RSS fields

Measured: congress-legislators has `rss_url` on 159 of 539 current terms and `contact_form` on 88. 155 `rss_url` values are on the member host. 4 are off host: 2 on `www.house.gov`, 1 on `www.theday.com` (a newspaper), and 1 on another member host. None of the 159 equals a configured feed URL. Examples are old CMS paths over `http`, such as `http://www.baldwin.senate.gov/rss/feeds/?type=all`, where we have no feed. The field is therefore stale. It is a hint only, never an input that code trusts.

### 2.3 Site signals

- RSS autodiscovery: `<link rel="alternate" type="application/rss+xml">` in the home page head. The collector `HtmlParser` reads only `a[href]`, so today it cannot return these links.
- Sitemaps: a `Sitemap:` line in robots.txt, or `/sitemap.xml`. The collector parses robots but returns only rules and the crawl delay. It does not return sitemap lines.
- Robots: the collector checks robots on every non-API fetch. A disallowed probe fails as robots evidence. McConnell shows this case today.
- Throttles: house.gov throttles load across its shared subdomains. The `house.gov` pacing group (3,000 ms) protects it. Britt and Kim refuse our requests on their press listings.

None of the site signals are "official" data in the dataset sense. They are on the official host, so their authority comes from the host rule.

## 3. Discovery flow

### 3.1 Trigger

A join is a current member whose bioguide ID no tracked person holds. Today `ProposeRosterChanges` computes joins and leaves from the latest roster snapshot and writes nothing. The flow keeps that rule and turns its output into durable proposals.

The 120th Congress starts on 2027-01-03 (assumed from the Constitution, not checked here). The 2026 election gives a large set of joins and leaves on that day. That is the practical deadline for Part A.

### 3.2 Steps

1. Roster preparation sees a join. In the same transaction as the snapshot, it writes one `source_discovery` job row per join (precedent: the roster store writes the photo jobs with the snapshot in one transaction).
2. A probe job fetches the home page from the official `url`, in page mode. Robots applies. Preparation routes the capture to an Application parser by resource, not to extraction and comparison (precedent: `ApiResource` routing in `JobPreparation`, and `ApiResponseReader` for saved captures). See D2.
3. The parser reads anchors, `<link rel="alternate">` feeds, and the `Sitemap:` hint. A second probe fetches `/robots.txt` or the named sitemap in page mode when the home page gives too few candidates.
4. Code ranks candidates with the path pattern table from section 1.3 (data, in a table). Each pattern names the role (`press`, `feed`, `about`, `issues`), the mode, and the profile to try. See D3.
5. For the best listing or feed candidate, a test job captures the listing once and one admitted article. Code runs the discovery parser and each candidate profile on the capture.
6. Code writes one `source_proposals` row per candidate with its checks and their evidence attempt IDs. Code activates a proposal only when every check passes (section 3.3). Otherwise the proposal waits for a person on the Sources page.

### 3.3 Checks before a source becomes active

| Check | Rule | Fact it rests on |
|---|---|---|
| Same host | The candidate host equals the member `url` host, after `www.` normalization | Official dataset value in the cited roster snapshot |
| Single member host | The host is a subdomain of `house.gov` or `senate.gov`, not `www.house.gov` or `www.senate.gov`, and no other tracked person has that host | Roster snapshot and person table |
| Agreement | When D1 picks both datasets: Congress.gov gives the same host | Second official source |
| Robots | The test capture succeeded, so robots allowed it | Collector evidence |
| Listing parses | Discovery status `parsed`, with at least 3 in-scope article URLs under the allowed prefix | Discovery evidence |
| Profile match | One known profile extracts nonempty text from the test article, within length bounds, and no other profile is ambiguous | Extraction evidence |
| Not a duplicate | No active source of the person has the same role | Source table |

A failed check never deletes the proposal. It keeps the reason, as `held_reason` does for statements.

### 3.4 When a person must approve

- Any failed check above.
- A host outside `house.gov` and `senate.gov`. Today no member has one, but the rule must hold for the next case.
- A new profile. Profiles are D-12 work.
- `statementTextInFeed`. The existing rule says only a person sets it.
- Executive officials. The White House feeds list other people, so they never get `officeOf`.

Each of these is a flagged manual step on the Sources page, with the reason.

### 3.5 Setting `officeOf` safely

`StatementCodeApproval` trusts `officeOf`. A wrong value lets code approve a statement in the wrong name. The guard has three layers:

1. Code sets `officeOf` only when the same host and single member host checks pass. That is a checked fact: "this URL is on the official website of member B, as the cited roster snapshot states."
2. The decision row records the actor `civic-lens:code`, the roster snapshot attempt ID, and the test capture attempt IDs (precedent: `ReviewRecords.AppendCodeApprovedAsync` and the `civic-lens:code` actor).
3. The existing statement checks still run: one speaker who equals `officeOf`, every quote found once, gate pass, and the date window.

A model never proposes `officeOf` and never ranks a candidate into activation. A model prediction is never a human approval.

Risk that stays: a member site can host pages written by others, such as news clips. The press path patterns lower this risk. The same risk exists in the hand-made file today.

### 3.6 Collector boundary

The flow uses only page, html, and feed jobs that exist. The collector gets no database access and no registry data. All parsing of home pages and sitemaps is in Application, from saved captures. Option D2 (a) would change the wire protocol. The recommendation avoids it.

## 4. Leaves

A leave is a tracked person with a bioguide ID who is not in the latest roster snapshot and has no configured offices. `ProposeRosterChanges` already has this rule, and it never reads an empty snapshot as everyone leaving.

Proposed behavior:

1. Code ends the coverage of each source of the person. `endsBefore` is the observation date of the first snapshot that lacks the person. See D5.
2. The schedules of those sources stop, as for a disabled source today. Admitted jobs finish from their snapshot.
3. Sources, `officeOf`, evidence, and records stay. `officeOf` keeps its meaning for statements captured during the term.
4. A person who moves to an executive office (as `jd-vance` did) is not a leave, because they have offices.
5. A returning member is a join with a known person ID. Discovery runs again. Old sources stay ended, and new ones get new IDs.

A failed fetch never ends coverage. A site that goes away after a leave only adds failed attempts until the schedule stops.

D-16 (party count for a member who left) and D-11 (past votes of a member added later) touch the same dates. They stay separate decisions.

## 5. Cadence

### 5.1 Check history we hold

| Table | Columns that matter | Kept |
|---|---|---|
| `collection_attempts` | `source_id`, `requested_url`, `observed_at_utc_ticks`, `outcome`, `capture_sha256`, `response_status_code`, `failure_code` | Forever. Insert only |
| `collection_jobs` | `job_id`, `source_id`, `is_root`, `created_at` | Forever |
| `evidence_processing` | `job_id`, `attempt_id`, `source_id`, `stage`, `outcome` (`Baseline`, `Unchanged`, `Changed`), `extraction_id`, `comparison_id`, `admitted_count`, `deferred_count`, `duplicate_count` | Forever |
| `document_extractions` | `attempt_id`, `extraction_version`, `text_sha256`, `profile_json` | Forever |
| `collection_discoveries` | `attempt_id`, `discovery_json` (full URL list) | Forever |
| `collection_candidate_jobs` | `source_id`, `url`, `job_id` (first admission of each URL) | Forever |
| `collection_source_schedules` | `interval_seconds`, `next_due_utc_ticks` | Current state only |

Measured from the schema docs: no table holds a change rate or a cadence. No retention job deletes any of these rows. D-04 proposes to delete raw bytes after a period but keep the observation record. The estimator needs only the rows above, so D-04 does not remove its input. A new profile revision after D-04 cannot recompute old text, so the history of that source restarts at the new revision.

### 5.2 Change signal

- Persistent page: a check is "changed" when its root job extraction has a different `text_sha256` than the previous root extraction with the same extraction version and profile revision. A raw hash is too noisy, because page markup carries tokens and dates. A processing gap or a new profile is a baseline, not a change. Failed and deferred attempts are not observations. Article jobs share the listing `source_id`, so the query must keep root jobs only.
- Listing or feed: the signal is the count of new URLs in each parsed discovery, compared with the union of earlier discoveries of the source. This is a count, not a yes or no. It stays exact while fewer new items arrive between checks than the listing shows. `admitted_count` is a poor proxy, because `maxJobs` 2 caps it and rechecks also count.

### 5.3 Estimator

From the secondary source (measured: Grimes and O'Brien quote it): Cho and Garcia-Molina, section 4.2, reduce the bias of the naive estimator X/T. The bias comes from more than one change inside one check interval, which shows as one change. Their estimator for regular checks, as Grimes and O'Brien print it, is `-1/log((T - X + 0.5)/(T + 0.5))` for the mean time between changes, with T the observed time in check intervals and X the number of changed checks. The rate per interval is then `r = -log((n - X + 0.5)/(n + 0.5))` for n checks. Grimes and O'Brien say it has much lower bias when the true rate is near 0.5 to 2 changes per interval.

From memory, unverified: the same paper gives a maximum likelihood estimator for irregular check intervals, solved by iteration, and an estimator for sources that give a last-modified date. Our intervals are irregular, because pacing, retries, and downtime move each check. Our member pages send no validators today, so the last-modified case does not apply.

Grimes and O'Brien also found that fast pages are often not Poisson. Many change on hourly or daily clocks, and the rate depends on local time and weekday. This is a risk for listings, which may publish on work days only.

For listings the count signal permits the simple Poisson rate `lambda = new items / time`, which has no interval bias while the listing window holds all new items. Small samples need a prior. A gamma prior pooled by role and chamber gives `lambda = (items + a) / (time + b)`. This is our proposal, not part of the Cho and Garcia-Molina work.

### 5.4 Is the history enough?

Assumed, not measured: if production checked every source since 2026-10-10, a page has at most about 104 checks (26 days at 6 hours), and a listing at most about 624. Most about and issues pages likely change less than once a month. Then X is 0 or 1 for most pages, and the estimate rests on the prior. The estimator will separate fast listings from slow ones much sooner than it separates slow pages from very slow pages. Section 5.6 measures this before any design is fixed.

### 5.5 Budget and pacing groups

No aggregate collection budget exists today. Each job reserves its own requests, bytes, and timeout. The spending total is planned in R-24. The binding limit for member sources is the pacing group:

- A 3,000 ms group admits at most 1,200 jobs an hour.
- Scheduled `house.gov` roots today: 720 pages every 6 hours (120 an hour) plus 418 listings and feeds every hour, so about 538 an hour. Up to 2 article jobs per listing check come on top. collection.md gives the same 538 figure and about 45 percent of capacity.
- `senate.gov`: 159 pages every 6 hours (about 27 an hour) plus 95 listings and feeds, so about 122 an hour.

Proposed rule: each pacing group gets a check budget, a share of its capacity, for example 50 percent, minus a reserve for article, discovery, and backfill jobs. A source outside any group gets a budget on its own origin. Cadence stays within `[60 s, 604,800 s]`, the bounds that the schedule table already checks, and within role bounds that D8 sets. The allocation is one pure function in Application with no I/O (precedent: `CollectionImportPolicy` and `DiscoveryAdmissionPolicy.OpenBudget`). The scheduler reads the result as it reads the bill cursor today (precedent: `ReadCursorAsync` in `CreateScheduledDefinitionAsync`).

From memory, unverified: Cho and Garcia-Molina show that, for average freshness, a uniform policy beats a policy proportional to the change rate. The optimal policy solves a Lagrange condition: each source gets the check rate at which the marginal freshness gain equals one shared multiplier. Very fast sources can get fewer checks than slower ones. Our goal differs from freshness. We want to see each version and each new press release before a deletion. D8 therefore proposes a miss bound.

Today `AdvanceDueAsync` throws when the schedule interval differs from the retained configuration. A measured cadence must replace that check, because the interval no longer comes from configuration.

### 5.6 Measure first on real history

These read-only queries run on production. Running them is a manual step for the owner, because this sandbox has no production access. Ticks convert with `to_timestamp((ticks - 621355968000000000) / 1e7)`.

```sql
-- Q1. Span and captured root checks for each source.
SELECT j.source_id, count(*) AS checks,
       to_timestamp((min(a.observed_at_utc_ticks) - 621355968000000000) / 1e7) AS first_seen,
       to_timestamp((max(a.observed_at_utc_ticks) - 621355968000000000) / 1e7) AS last_seen
FROM collection_jobs j
JOIN collection_job_attempts ja ON ja.job_id = j.job_id
JOIN collection_attempts a ON a.attempt_id = ja.attempt_id
WHERE j.is_root AND a.outcome = 'captured'
GROUP BY j.source_id;

-- Q2. Page changes on root jobs: changed, unchanged, baseline counts.
SELECT ep.source_id,
       count(*) FILTER (WHERE ep.outcome = 'Changed')   AS changed,
       count(*) FILTER (WHERE ep.outcome = 'Unchanged') AS unchanged,
       count(*) FILTER (WHERE ep.outcome = 'Baseline')  AS baselines
FROM evidence_processing ep
JOIN collection_jobs j ON j.job_id = ep.job_id AND j.is_root
WHERE ep.attempt_id <> 'prepare'
GROUP BY ep.source_id;

-- Q3. New listing URLs by day, from the first admission of each URL.
SELECT c.source_id,
       date_trunc('day', to_timestamp((j.created_at - 621355968000000000) / 1e7)) AS day,
       count(*) AS new_urls
FROM collection_candidate_jobs c
JOIN collection_jobs j ON j.job_id = c.job_id
GROUP BY 1, 2;

-- Q4. Failed and deferred root checks by pacing key, for the budget reserve.
SELECT j.pacing_key, a.outcome, a.failure_code, count(*)
FROM collection_jobs j
JOIN collection_job_attempts ja ON ja.job_id = j.job_id
JOIN collection_attempts a ON a.attempt_id = ja.attempt_id
WHERE j.is_root AND a.outcome IN ('failed', 'deferred')
GROUP BY 1, 2, 3;
```

Check before use: the `created_at` unit of `collection_jobs` (ticks or another unit), and whether Q2 needs a filter on `stage`. Exclude the first day of each source in Q3, because the first check admits the backlog. Q3 also shifts arrival times, because `maxJobs` 2 defers new URLs. PR 2 replaces Q3 with a discovery-based count.

Pass criteria for the measure step (proposed): the distribution of X per page and of new items per listing day, the share of sources with X = 0, and the weekday and hour pattern of listing arrivals. If most pages have X = 0, the first cadence release moves only listings, and pages keep the role default.

A second measure for Part A: run the discovery rules offline on saved home page captures of the 538 current members, and compare the proposals with the hand-made 1,392 sources. This gives recall and precision before any source activates. It needs one probe capture per member, about 538 house.gov and senate.gov jobs at Backfill priority. That fits in under one hour of group capacity.

## 6. Decisions for the owner

**D1. Trigger and official website source.** Problem: discovery needs a trusted official host for each join. Options: (a) the congress-legislators roster snapshot that we capture daily (CC0, volunteer data, `url` agrees with all 1,392 sources); (b) the Congress.gov member list with `fromDateTime` (official, needs the key, field name and filter unverified); (c) (a) as the trigger and (b) as a second check, with disagreement sent to a person. Tradeoffs: (a) needs no new source but rests on a volunteer project; (b) is official but adds a source and two unverified facts; (c) costs one more API source and gives the strongest basis for `officeOf`. Recommendation: (c), with (a) in the first PRs and (b) added in PR 8 after a live check. Precedent: the bill list cursor (`BillStreamWindow`) for `fromDateTime` polling.

**D2. How a probe fetches and parses.** Problem: the collector returns only `a[href]` links. Options: (a) a new collector discovery mode that also returns feed links and sitemap lines (wire change and protocol version 2); (b) a page mode probe whose saved capture an Application parser reads, routed by a resource value so that it skips extraction and comparison; (c) html mode on the home page (no feed links, no sitemap). Tradeoffs: (a) moves parsing into the collector and needs a compatibility decision; (b) keeps the collector unchanged and bounded, but adds one resource route; (c) misses feeds. Recommendation: (b). Precedent: `ApiResource` routing in `JobPreparation`, and the roster parsers that read saved captures.

**D3. Candidate ranking.** Problem: pick the press listing, feed, about, and issues pages from a home page. Options: (a) a path pattern table built from the 1,392 hand-made sources; (b) a model classifier; (c) (a) with a model hint that a person sees. Tradeoffs: (a) is checkable, and three paths cover 332 of 349 House listings; (b) is broader but is a prediction, so it can never activate a source; (c) adds cost for the long tail. Recommendation: (a), patterns as table rows with a revision ID. Precedent: document profiles as configured data with retained revisions.

**D4. Activation and `officeOf`.** Problem: statement code approval trusts `officeOf`. Options: (a) code activates and sets `officeOf` when every check in 3.3 passes; (b) code activates, but `officeOf` waits for a person, so statements wait too; (c) a person approves every source. Tradeoffs: (a) automates the common case and rests on checked facts; (b) is safer but blocks code approval of new members until a person acts; (c) keeps today's manual step. Recommendation: (a) with the single member host rule. Precedent: `StatementCodeApproval` and the `civic-lens:code` actor, which approve only after checks.

**D5. End date of a leave.** Problem: the roster gives the planned term end, not the real end after a resignation or a death. Options: (a) the observation date of the first snapshot without the person; (b) the `ends_on` of the last term in the last snapshot with the person; (c) store both, and use (a). Tradeoffs: (a) is observed, but late by up to one day; (b) can be months wrong after an early leave; (c) costs one more column. Recommendation: (c). Precedent: the roster rule that an empty snapshot never reads as a leave.

**D6. Change signal.** Problem: what counts as a change. Options: (a) raw capture hash; (b) extraction text hash within one extraction version and profile revision for pages, and new URL count for listings; (c) comparison hunks after the R-13 churn rules. Tradeoffs: (a) counts markup noise; (b) is available now; (c) is better but needs R-13. Recommendation: (b) now, (c) when R-13 lands. Precedent: comparison uses the preceding compatible observation and treats a gap as a baseline.

**D7. Estimator.** Problem: estimate a rate from few, irregular checks. Options: (a) naive X/T; (b) the Cho and Garcia-Molina bias-reduced estimator for regular checks; (c) the irregular interval maximum likelihood estimator; (d) for listings, the Poisson count rate with a gamma prior pooled by role and chamber. Tradeoffs: (a) is biased low when checks are slow; (b) assumes regular checks; (c) fits our intervals but needs iteration and the paper text; (d) is simple and exact while the listing window holds the new items. Recommendation: (d) for listings and (c) with a pooled prior for pages, after the owner reads the original paper. Version the estimator as a behavior version.

**D8. Objective and allocation.** Problem: what the cadence optimizes. Options: (a) uniform, as today; (b) proportional to the rate; (c) Cho and Garcia-Molina freshness optimum by Lagrange multiplier; (d) a miss bound: choose each interval so that the chance of k or more events in one interval stays below a set value (k = listing window for listings, k = 2 for pages), then scale all intervals by one shared factor until each group fits its budget. Tradeoffs: (a) wastes checks on slow pages; (b) is worse than uniform for freshness (unverified, from memory); (c) maximizes freshness, which is not our goal; (d) matches the goal of seeing each version and each release, and is easy to explain in a method note. Recommendation: (d), with role bounds, for example listings 1 to 24 hours and pages 6 hours to 7 days.

**D9. Budget unit.** Problem: no aggregate collection budget exists. Options: (a) a share of each pacing group capacity; (b) the spending total from R-24 in requests and bytes; (c) both. Tradeoffs: (a) protects house.gov now; (b) needs R-24. Recommendation: (a) now, (c) after R-24.

**D10. Where cadence lives.** Problem: the schedule interval must stop coming from configuration. Options: (a) an append-only `source_cadence_estimates` table with the inputs (n, X, time, items), the estimator version, and the chosen interval, which the scheduler reads; (b) a column on `collection_source_schedules`; (c) values written back into the source rows. Tradeoffs: (a) keeps the reason for each interval and can be audited; (b) loses history; (c) makes each estimate a new source revision and churns revision IDs. Recommendation: (a). Precedent: roster snapshots, which are append-only and cite their input.

## 7. PR plan

Each PR stays small, has tests, deletes what it replaces, and updates the feature docs in place. "Tables" means it depends on the officials-in-tables work.

| PR | Content | Tests | Deletes | Depends on |
|---|---|---|---|---|
| 0 | Owner runs Q1 to Q4 on production. Manual step. No code | None | None | None |
| 1 | `CadenceEstimator` (pure, Application) and a read-only `cadence report` Host command that prints estimates and proposed intervals per source. Production caller is the command (precedent: `roster propose`) | Simulated Poisson histories with known rates; bias within bounds; X = 0 and X = n cases; irregular intervals; root jobs only; a profile change is a baseline | None | PR 0 result |
| 2 | Discovery-based new URL count for listings, from `collection_discoveries` | Union rule; first check is a baseline; a 304 adds no observation | Q3 workaround in docs | PR 1 |
| 3 | `source_cadence_estimates` table, a daily estimate pass, and the scheduler reads the latest interval. Role defaults replace `checkIntervalSeconds` | Scheduler uses the estimate; bounds hold; a missing estimate uses the role default; migration drift test | `checkIntervalSeconds` on 879 sources, the interval equality throw in `AdvanceDueAsync`, the field docs | D7, D10 |
| 4 | Group budget allocation (D8, D9) in the estimate pass | Each group fits its budget; scaling is monotonic; a group with free capacity keeps the miss bound intervals | None | PR 3 |
| 5 | Probe resource, home page parser, sitemap hint, pattern table, and a read-only `sources propose` command. Offline recall check against the 1,392 sources | Fixture home pages for House Drupal, WordPress, and Senate layouts; robots refusal; off-host links dropped | The manual press note in `ProposeRosterChanges` output, when PR 6 lands | D2, D3 |
| 6 | `source_proposals`, test captures, the checks in 3.3, and code activation with `officeOf` | Each check fails alone and holds the proposal; a shared host waits for a person; the decision cites its attempts; a statement from a new source is code-approved only with `officeOf` | Manual join step in docs and operations | Tables, D4 |
| 7 | Leaves: end coverage, stop schedules, keep sources | Empty snapshot ends nothing; a person with offices is not a leave; both end dates stored | The `leaves` list of `roster propose`, if no other reader remains | Tables, D5 |
| 8 | Congress.gov member list source with `fromDateTime`, and the agreement check | Cursor replay; disagreement waits for a person | None | D1, live field check |

PRs 1 to 4 do not depend on the tables move. Cadence is keyed by source ID and can ship first. PRs 6 and 7 need sources and coverage as rows, because code must write them at run time.

## 8. Risks and open questions

Measured facts:

- All 1,392 member sources sit on the host of the member `url` in congress-legislators.
- `rss_url` is present for 159 members, matches no configured feed, and is off host for 4.
- The coverage file has 1,403 sources, 9 of them `api`. The doc says 8.
- The collector parses only `a[href]` and returns no sitemap lines.
- No table holds change rates. The history tables keep every row.

Assumptions and unverified items:

- The original Cho and Garcia-Molina papers were not read. The irregular interval estimator and the refresh policy results come from memory. This blocks D7 and D8.
- The Congress.gov member field name and the `fromDateTime` filter on the member list.
- Congress.gov data terms beyond the rate limit.
- How the first coverage file was made.
- How much history production holds, and whether the worker ran without gaps since 2026-10-10.
- The join and leave volume on 2027-01-03.

Risks:

- A wrong `officeOf` lets code approve a statement. The single member host rule and the statement checks guard it. A site redesign that moves press to a new host sends the member back to a person.
- Listings may follow weekday and hour clocks, not a Poisson process. A rate per hour of week may be needed later.
- Slow pages have X = 0 for weeks, so their cadence rests on the prior.
- A longer page interval delays the detection of a removed position. D8 role bounds cap that delay.
- Mixed configuration revisions already let one host hold two pacing keys for a short time. Cadence changes must not widen that window.
- House.gov load rises with probes in January. Probes run at Backfill priority, inside the group budget.

Open questions:

- Should a new member start at the role default or at a faster first week to build history quickly?
- Should cadence react at once to a burst of new items, or only at the daily estimate pass?
- Does the owner want the Sources page to show each estimate with its inputs, as a coverage limit on public pages?
