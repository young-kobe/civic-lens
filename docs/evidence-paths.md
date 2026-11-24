# Evidence Paths

Evidence Paths is the Civic Lens plan. It holds the direction, the phases, the active work, the accepted backlog, and the pending decisions. Merged code, deployed behavior, and accepted reader flows are separate states. Remove completed work here and document implemented behavior in the feature references. Start at the [architecture lookup table](architecture.md#lookup-table).

## Direction

Civic Lens lets a citizen follow a public claim through an official decision to a measured outcome. Start with a question that matters to the reader. Give each answer a date, an accountable actor, and a one-step path to the original evidence.

One [relationship pipeline](evidence-paths/pipeline.md) connects records across many dimensions of the data, implemented across four explicit data planes. It aggregates connections by official, issue, bill family, committee, and time, and shows them in an informative, interactive way. The aim is to make inaccessible or hard-to-connect public records visible to a regular reader. The site shows what the records say and how they connect. It never states a conclusion. The reader draws the conclusion. The same rules apply to every official, issue, and dimension, so no single case or topic shapes the output. Single cases, such as the VA claims backlog, test the pipeline. They are not the product.

The audience is regular citizens who want transparency in U.S. national politics. State and local coverage are possible future extensions. Readers start with reviewed accounts and can inspect the supporting evidence in one step. The editorial flow and Mosaic v2 identity are agreed.

Data is the product. The product is only as good as its data, so data and database schemas are a first-class feature. Design them as deliberately as any public page. The goal is typed, version-bound links between people, records, bills, provisions, votes, sponsorships, executive actions, document changes, issues, and outcomes.

Design for scale under a solo maintainer. Automate every step that does not need human judgment. Code approves official fact records and deterministic structural paths. The product scales on deterministic facts and rule-gated aggregates, while human-reviewed judgment links stay bounded to sustainable solo-maintainer review capacity. A model prediction is never an approval.

Teaching the reader is a first-class goal. Each page answers more questions than it opens (owner, 2026-11-06) and explains what each record is and why it matters in plain words. Where a bill, vote, action, or term is ambiguous or technical, the page gives an AI summary in plain words with its AI label. Code checks each summary against its source, targeting grade 12 and rejecting above 14. Use three-layer progressive disclosure so regular readers see plain clarity and one guardrail line first, interactive exploration second, and full audit metadata on demand.

Derived insights make Civic Lens more than a connector to source APIs. Code computes each metric and publishes a method note, denominator, base rate, and coverage limit. A person reviews any judgment before publication. [Candidate features](evidence-paths/features.md) lists the insights and features to investigate. Investigation can remove any of them.

## Current stage

Civic Lens is deployed and past its MVP and first deployment. Work now improves the running product through reviewed, tested iterations. Current capabilities and ownership are in [architecture](architecture.md). Deployment, backups, recovery, and live checks are in [operations](operations.md).

R-68 is the active priority. It closes the evidence, production, and reader obligations for the deployed redesign under the [narrowed November 24 closeout scope](#active-r-68-redesign). `PublicApi` serves every public page, and the publisher writes data only. The zero-cost setup experiments and private evidence pilot can run independently on frozen data. The other phases are the proposed build-out. Items in the [work queue](#work-queue) keep their dependencies. Move accepted behavior into its feature reference as each phase ships.

## Phases

| Phase | Deliverable | Evidence for moving forward |
|---|---|---|
| 0, across all phases. [Relationship pipeline](evidence-paths/pipeline.md) | Four data planes sharing one evidence-reference contract, ordered candidate sources, solo-maintainer review and statistical gates, monotonic withdrawal tombstones, and display rules | Each displayed connection states its type, role, basis, and plain limit, binds exact evidence, and survives withdrawal and rollback on every surface. |
| 1. [Claims and evidence](evidence-paths/claims.md) | A reproducible outcome audit and a multi-issue private pilot of statement, provision, and vote links | A maintainer can reconstruct each finding from frozen source versions, verify speaker and role controls, and explain remaining gaps. |
| 1, from the first case. [Evaluation and observability](evidence-paths/evaluation.md) | A private run comparison, a pre-registered seeded holdout with intra-rater reliability checks, and a public methods summary | Show which stage improved, the effect on critical attribution errors and coverage at realistic corpus scale, and the review minutes per accepted finding. |
| 2. [Graph and statistical research](evidence-paths/graph.md) | Typed paths across records using `LATERAL` SQL reads, optional `ReleaseIngest` ranged projections if measured latency requires them, and bounded retrieval comparisons | Each additional correct link survives review and leads to cited evidence. Measure SQL plans and maintenance cost before adding any new store. |
| 3. [Reader experience](evidence-paths/reader.md) | Three-layer progressive disclosure, a three-state Evidence Path mockup, code-selected milestone feed, and tested navigation against the shipped DR6 header | First-time readers explain what happened, avoid false provision inferences on package or procedural votes, understand insufficient evidence, and reach sources in one step. |
| 4. [Interactive evidence route](evidence-paths/delivery.md) | One issue route on `PublicApi` and `PublicWeb` with progressive JSON-backed panels and release-consistent withdrawal handling | Navigation works with JavaScript disabled. Deep links, monotonic withdrawals, rollback safety, privacy, and panel failure states pass the route trial. |

The [evidence interface](evidence-paths/evidence-interface.md) shows which reader questions can use existing joins and which need additional classified evidence. Keep `config/issues.json` as the authoring source and keep the current `release_issues` copies without a versioning migration until measured storage or latency justifies one; fix missing legacy definitions directly in `PublicationCatalog`.

## First assignment

First run the [setup experiments](evidence-paths/pipeline.md#before-the-pilot-opens-its-holdout) in priority order, starting with speaker controls, frozen release export and input bundle, the `RelationshipRevision` binding dry run, and the full-corpus distractor retrieval check. Then start a fresh branch from `origin/main`. Read the [pipeline rules](evidence-paths/pipeline.md), [claims workflow](evidence-paths/claims.md#proposed-workflow-and-ownership), [relationship review](architecture/statements.md#relationship-drafting), [evaluation plan](evidence-paths/evaluation.md), and [trial results](evidence-paths/experiments/evidence-trials/results.md). Use the frozen published release export and pinned external bundle in development.

Build one private review flow extending the existing relationship-drafting and review handlers under settled decisions D-19 and D-20. Put the attributed quotation, speaker origin, and date beside the exact bill edition and passage span, the roll-call question and motion polarity, and the member's recorded cast when one exists. Let the maintainer accept, reject, or record `InsufficientEvidence` (with conditional reopen on evidence fingerprint change). Bind the decision to the evidence revisions, candidate-set digest, retrieval method version, and `as_of_utc` cutoff so any replay reproduces it. Keep public contracts unchanged until the pilot passes.

Demonstrate the flow on a supported direct-motion link, a package or procedural vote link, a wrong-speaker control, and a tempting wrong link. Then test on the pre-registered seeded holdout of unseen bill families sized under the [pipeline evaluation](evidence-paths/pipeline.md#evaluation), using blinded intra-rater re-labeling and an LLM devil's-advocate rubric check (never an approval) in place of a second human annotator. Capture candidate coverage in the first five slots, unsupported attribution rate and risk, wrong-speaker count, abstention rate, and review minutes per accepted link. Keep cases without a sufficient vote and retrieval failures in the denominator. All nine prior trial families are development data and stay out of the holdout.

Hand back the private workflow in its PR with a `--bundle` replay command, pinned inputs, baseline and candidate counts, intra-rater kappa, and decisions a replay can reconstruct. List unresolved cases and the missing evidence for each. The pilot can finish with an inconclusive result. Use the [evaluation criteria](evidence-paths/evaluation.md#pilot-decision-and-feedback) to decide whether to retain the lexical baseline, refine a specific stage, or scope a public trial. Before any public Evidence Path ships, build the three-state reader prototype and run the 6-person think-aloud test in [reader decisions](evidence-paths/reader.md#decision-test).

## Experiments

| Experiment | Input type | Result and repeatable materials |
|---|---|---|
| [Release 1470 production baseline audit](evidence-paths/experiments/release-1470-audit.md) | Operational DB read-only observation (release 1470) | Active release has 19,649 bills, 1,597 votes (50 question tuples), 694 executive actions (100% body text), 33 statements, and 0 relationships. Found 965 of 1,059 statement drafts held (255 pre-`officeOf`, 269 date-window with verified spans, 428 multi-speaker), 100% of 1,510 bill passage actions matched by 4 methods, `release_issues` at 1.8 MB across all 1,470 releases, and correlated `LATERAL` 3-hop cast queries at 13.7 ms (2 bills) and 115.1 ms (20 votes). [LATERAL query](evidence-paths/experiments/lateral-cast-query.sql) and [D-22 export](evidence-paths/experiments/export-frozen-release.sql). |
| [AI and statistics on existing evidence](evidence-paths/experiments/applied-methods.md) | Public download and model weights | Tested agreement between two retrievers, hierarchical clustering of 876 bill sections, and time to a committee report for 64 bills. The [research priorities](evidence-paths/graph.md#which-ai-or-statistical-study-comes-next) tie each next comparison to a product decision. |
| [Versioned issue definitions](evidence-paths/evidence-interface.md#can-a-versioned-table-replace-the-repeated-definitions) | Operational DB observation and offline SQLite fixture | Twelve retained releases contained 192 issue rows and 16 distinct public definitions. Measured repetition only, not a bottleneck. Migration dropped from active plan. |
| [Reuse of issue assignments across features](evidence-paths/evidence-interface.md#offline-interface-check) | Offline SQLite fixture (casts unverified until setup check) | Three House votes supported two reader views. Adding a poll supplied two contextual matches and zero eligible directional comparisons. [Runnable SQL](evidence-paths/experiments/reuse-issue-facts.sql). |
| [Five linked-evidence trials](evidence-paths/experiments/evidence-trials/results.md) | Public downloads, model weights, live API observation | Tested quote retrieval, provision continuity, charging delivery, and coalition recurrence. Exercised all reader form assignments at two widths. Human comprehension remains unmeasured. |
| [Published graph on two House bills](evidence-paths/experiments/published-graph.md) | Frozen release 1416 fixture | In release 1416, 338 sponsorships produced 56,281 member pairs. The same common backer appeared in SQL and NetworkX. [Query](evidence-paths/experiments/published-graph.sql) and [analysis](evidence-paths/experiments/project-sponsors.py). |
| [Clustering bill provisions across bills](evidence-paths/experiments/section-clusters.md) | Public downloads | Clustered 876 sections in eight editions (base cross-bill co-assignment 4.1% at 12 K-means clusters, silhouette 0.031, mean seed ARI 0.393). Ranked neighbors outperformed clusters. No more clustering planned. |
| [Amendment or motion versus final passage](evidence-paths/experiments/paired-votes.md) | Public downloads | Full roll-call counts: 7 of 100 senators cast differently in the insulin case and 9 of 95 in the immigration case. Establishes the need to show exact motion questions. |
| [Issue mapping across legislative subjects](evidence-paths/experiments/issue-mapping.md) | Public downloads and operational observation | Tested statement-to-provision retrieval and topic candidates on four bill editions. Verified four roll-call questions and seven selected casts. |
| [Veterans claims backlog at two cutoffs](evidence-paths/experiments/va-backlog.md) | Public downloads | VA's inauguration-day press figure gives a 72.07% decline by Oct 10 and 69.95% by Nov 7. Both weekly workbook baselines exceed 70% by Oct 10 and fall short of 70% by Nov 7. [Workbook calculation](evidence-paths/experiments/va-backlog.py). |

## Which research would change the next build?

The [five completed trials](evidence-paths/experiments/evidence-trials/results.md) establish the starting point. Only the first row and the setup checks belong to the private pilot. Reopen the other questions when the named trigger occurs and the result would change a product decision.

| Question | What the completed trial established | Next decision and evidence needed |
|---|---|---|
| Can a maintainer connect a quote to the right decision at real corpus scale? | Explicit links recovered 4 of 4 selected pairs on 6 bills and 5 roll calls, but two pairs treated a whole-bill passage vote as provision evidence. Every method returned candidates on 3 of 3 insufficient cases because none could abstain. | Run speaker controls and full-corpus distractor retrieval first. Then compare role-aware methods against BM25 plus explicit bill-vote join and a calibrated abstention threshold on unseen families. Score coverage, attribution rate, attribution risk, wrong speaker, and review minutes separately. |
| Did a provision survive into the next edition? | Text matching got 8 of 8 continued provisions right and 1 of 2 dropped provisions right. The one false continuation changed legal effect. | Trigger: a change timeline is scheduled. Test at least 29 dropped or changed-mechanism transitions from unseen families. Keep uncertain transitions in human review. |
| Did a funded project reach delivery? | The Deerfield case linked an obligation and a reported opening, though the opening report omitted the FAIN. | Trigger: a delivery stage is scoped. Test 5 recipients including similar-name decoys. Until then, show each verified stage and its source. |
| Do shared sponsors form a recurring group? | Removing the most widely backed early bill cut later overlap from 22 of 58 repeated pairs to 1 of 15 (consistent with the degree null). | Do not run now. Revisit only with full-session windows, leave-one-out range across all bills, and a party/committee-preserving null. |
| Do readers understand linked evidence and avoid false inferences? | The two-order instrument ran with zero participants and tested neither quote-to-vote links nor insufficient evidence. | Build the three-state Evidence Path mockup and run a 6-person think-aloud before any Phase 3 UI build. Test navigation separately against the shipped DR6 header. [Reader trial](evidence-paths/reader.md#decision-test). |

## Decisions to take in order

1. **Lock solo-maintainer pipeline foundations (settled below as D-18 to D-24).** Run the zero-cost setup experiments first, especially speaker controls on multi-speaker releases, frozen release export and bundle, `RelationshipRevision` binding dry run, and full-corpus distractor retrieval. Start the private pilot on frozen data with no public contract change. Keep the current `release_issues` table without a versioning migration; fix missing legacy definitions in `PublicationCatalog`.
2. **Scale Plane 1 deterministic facts and Plane 3 aggregates in parallel with Plane 2 pilot work.** Prioritize code-approved structural features that need zero per-record human review: motion polarity and procedural effect on votes, edition-bound sponsorships with shell-bill detection, voice/UC "how it passed" (R-69), bill fate vs. base rate (R-69), and the source coverage meter (R-56).
3. **Keep PostgreSQL `public_api` views with `LATERAL` reads as the graph layer.** Only if a named query exceeds latency budgets with valid `LATERAL` SQL, add revision-ranged projection rows to `CivicLens.PublicApi`'s search database via `ReleaseIngest`. Do not add Apache AGE or Neo4j without a measured bottleneck. [Graph research](evidence-paths/graph.md).
4. **Build the three-state Evidence Path mockup and run the 6-person think-aloud before Phase 3 UI work.** Use three-layer progressive disclosure so regular citizens see plain clarity and one guardrail line first and audit metadata in a collapsed drawer. Test any new left rail against the shipped DR6 header as control. [Reader decisions](evidence-paths/reader.md#decision-test).
5. **Ship the first interactive Evidence Path route inside `PublicApi` and `PublicWeb` (Phase 4).** Use Razor SSR with server-rendered navigation and progressive JSON-backed panels. Add a release-independent withdrawal tombstone ledger so rollbacks cannot resurrect withdrawn records. Reopen a separate frontend shell only if a measured reader need fails on the current stack. [Delivery trial](evidence-paths/delivery.md#acceptance-trial).

## Work queue

Each item gives its outcome, dependencies, and design reference. Surface new architecture and public wording choices before implementation. Do not reopen settled choices.

### Active R-68 redesign

R-68 serves every public page from `PublicApi`, rendered from restricted `public_api` views. Typed published data owns each fact. The publisher writes data only, Caddy serves release photos, public HTML is cached per release, and ZIP fragments remain private. The [public site reference](architecture/public-site.md) owns implemented reader behavior.

The owner narrowed closeout on November 24, 2026. The remaining R-68 work is the list below. Reuse completed checks unless the relevant code, data, or release selection changes. Incidental polish, return updates, and PR 639 remain outside this closeout. The earlier transfer of flat header/Menu, Bills/Votes primary-navigation placement, and Home featured-vote visual acceptance remains in force; their function, accessibility, privacy, and evidence obligations remain.

Completed evidence includes the bounded cold Home measurement, sampled member and issue readers, representative routes and data links, Maps, and the bounded shared-database workload. Historical bill recovery is complete. Flow 7 is closed using the existing native President navigation proof and the bounded live release-1470 count check: 607 documents for Donald Trump, with the four type counts matching published data. Further President helper work is stopped and moved to Evidence Paths.

- [ ] G3: store validated whole-roll-call totals on typed vote revisions through the existing stored-column and backfill workflow (code merged in PR #641; backfill and live check remain). Preserve tracked-member lists separately. Backfill and verify the selected Senate vote shows 52 Yea out of 100, rather than the tracked subtotal of 50 out of 98.
- [ ] G8: use each exact cited child revision's text origin on the server-rendered Brief page, preserving the API and release wire (code merged in PR #642; rollout check remains). Add the approved caption "Senate roll call {number}" using the existing official URL rule. Apply the three approved glossary corrections and Bill source link from the recorded wording review.
- [ ] G9: complete one real reader report. The owner submits the marked test; verify its stored identity and worker pull, then resolve it. The November 24 attempt returned the report-unavailable message, so diagnose that failure before another submission.
- [ ] G10: assess only the changed outputs against the retained owner criteria. Reuse the completed retained-surface review and unchanged reader evidence.
- [ ] G11: merge the reviewed changes with CI passing on their exact heads and perform the final rollout under the existing backup and deployment rules. Tell the owner immediately before rollout so the Anthropic account can be funded.
- [ ] G12: keep full rigor for final release files and rollback. Freeze publication, verify the original latest/active/served release and selected rollback release, activate and verify rollback, then restore the original release even on failure. Require latest, active, and served to agree before restarting the worker. Reuse unchanged immutable checks and run only invalidated verifiers.
- [ ] G13: verify final transport, privacy, and health after the last runtime change, and show the enabled model stages recover after funding. Run `check-live` last.

For the remaining gates, use one bounded live check per gate, automated review plus owner sign-off per change (solo maintainer), and exact-head CI as the test record. Do not add pinned-hash proof files beyond the deploy script's output. Preserve existing evidence without building more proof machinery. G12 retains the full rollback and restore checks.

All eight pending owner decisions were accepted on November 24. Decisions 5 through 8 are covered by G3 and G8 above. The following accepted work is **moved to Evidence Paths** and must not block R-68:

- G7 typed Search term context, moved to Evidence Paths with decisions 2, 3, and 4: nullable `expectedBillKeyTermCount` is null for unknown, zero for none, and one through five for an exact count; durable bounded Search progress serves complete results only and has a separate verifier; finite cancellation-aware `57P03` retry applies only to the initial Search writer connection open. Preserve the existing `search-term-context-local` worktree without resetting it.
- G6 fixed 40-case AI measurement, moved to Evidence Paths with decision 1: retain immutable per-case evidence IDs and actual per-case and per-pass prompt versions, within the existing 3M token cap. No paid sample is required for R-68 closeout.
- Further flow-7 President helper work, moved to Evidence Paths. The existing native navigation proof and bounded live count check satisfy this R-68 flow; no more helpers or helper reviews are required.

The model failures are an operational funding issue: the Anthropic account has no funds. They are not an outstanding design choice. No other remaining R-68 work needs paid calls. Production authority ends November 26 at 18:17 UTC; notify the owner early if G11 through G13 cannot finish within that window.

### Accepted plans

The owner accepted these designs. Each item holds its choices until the code and the feature doc replace them. Build each PR from `origin/main`.

**Public site**

- R-47: Finish the remaining bill insights: sponsored bills that moved and bipartisan cosponsorship, with clear bases and method notes. Existing boards, badges, bill-status lines, and prior-Congress fallback are implemented. Their public API and server-rendered replacements belong to R-68. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
- R-48: What an official said and how they voted. The profile lists pairs, not totals: the quote with its date, then the cast with its question, procedural effect, date, and result. It shows no Yea or Nay totals and no talk against action quadrant, because a bare total lacks context. The [Phase 1 pilot](#first-assignment) decides the evidence roles and limit wording of each pair. Depends on: R-47. [Publication](architecture/publication.md#limits-and-planned-work)
  - [ ] Said and voted pairs on the profile
- R-64: What officials did on an issue. Today a bill or vote links to an issue with no direction, so no official position can be derived. Add a row for what a bill does on an issue, with citations, and a structured procedural effect / motion polarity row for what a Yea does on each vote (pass, amend, table, recommit, cloture, waive rule). Code then lists each official's casts with that evidence. The page shows no score and no "pro" or "anti" label. Effect on an issue starts with facts only: bill status, vote result, and whether the bill became law. Start with one tracked issue and measure accuracy and solo-maintainer review load. The schema design goes to the owner first, using the [connection model](evidence-paths/pipeline.md#connection-model). Depends on: R-48, and the action redraft. [Publication](architecture/publication.md#limits-and-planned-work)
- R-69: Representation audit. Show how an official acts for their constituents, with the evidence side by side and its error named. Civic Lens never publishes a composite representation score or grade, its own or a scorecard's. Each feature has a method note. Prioritize the code-approved structural items (`How it passed` and `Bill fate`) first. The research is on branch `survey-representation-data`. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
  - [ ] How it passed: each bill and nomination shows roll call, voice vote, or unanimous consent, so readers see when no member's position is on record
  - [ ] Bill fate: the profile shows how far each sponsored or cosponsored bill got and when the official joined it (edition-bound), beside the chamber base rate, with no rank
  - [ ] District context: Census district facts with their margins of error beside votes on matching issues. The owner chooses where district facts are stored before this PR
  - [ ] Ideology and district: the NOMINATE score beside the district presidential margin, with a reviewed method note explaining agenda-control and intra-chamber relative scaling limits
  - [ ] Constituent opinion: survey estimates with error bars, only for an issue with a published estimate whose terms allow reuse, after the maintainer reviews the method note

**Review and approval**

- R-51: Delete the gate-label pages after D-13. The combined review queue, Take off site, and reports in the editor are implemented. Depends on: D-13. [Review](architecture/review.md#limits-and-planned-work)
- R-65: Move to the next queue item after a review decision. Keep typed text after a failed approval and preserve every approval check. The shared editor and Save and approve transaction are implemented. Depends on: R-51 take off site. [Review](architecture/review.md#limits-and-planned-work)
- R-52: Approve factual links by code. The types are "statement about a vote" and "describes own bill". The types "states position on" and "claims credit for" stay with a person. Under settled decision D-21, code approval of `StatementAboutVote` waits for the Phase 1 pilot result, vote-role labels, and multi-speaker attribution controls. Depends on: R-51, Phase 1 pilot. [Review](architecture/review.md#limits-and-planned-work)
  - [ ] Code approval of factual links after pilot and speaker-control gates pass, plus new public wording
- R-54: Finish bill issue narrowing. The model gets the Congress.gov legislative subjects when a bill has them, and the redraft command exists. Still open: run the subjects backfill and the redraft command on the live site. Then check on a sample that a bill with no fitting issue publishes with no issue. Depends on: none. [Actions](architecture/actions.md#limits-and-planned-work)
  - [ ] Run the subjects backfill and the redraft command
  - [ ] Draft the action backlog, and spot check the issue tags after it

- R-66: Gate model output on confidence. Models give a confidence score. A low score sends the record to the one-page review, where the maintainer fixes it or tells the model what to fix. Set the threshold from measured data and the pilot's labeled set (folding the pilot abstention threshold study here). Depends on: R-65, Phase 1 pilot. [Review](architecture/review.md#limits-and-planned-work)
  - [ ] Agree the design
  - [ ] Build the gate and the model fix
- R-67: Measure each prompt. Each prompt version gets a live, budgeted, opt-in measure on fixed bills, and the numbers go on the PR. Use the cheapest setting that holds accuracy. The order is vote, statement draft and gate, document change, relationship, then brief. Includes G6 fixed 40-case AI measurement moved from R-68. Depends on: none. [Analysis](architecture/analysis.md#limits-and-planned-work)
  - [ ] Agree one opt-in eval command. Later prompt measures extend `actions text measure` or replace it with one command
  - [ ] Measure the other prompts, one PR for each

**Coverage**

- R-55: Stop losing feed items. Items past `maxJobs` are dropped for good today, and every member feed uses `maxJobs` 2. Survey a drain of deferred candidates, then build it. Depends on: none. [Collection](architecture/collection.md#limits-and-planned-work)
  - [ ] Survey and build a drain of deferred candidates
- R-56: Publish source coverage. Add a coverage field for each official and a "sources we could read" panel, so uneven coverage is visible. Prerequisite before publishing cross-official Evidence Path aggregations. Depends on: none. [Publication](architecture/publication.md#limits-and-planned-work)
- R-57: Finish the executive and judicial sources, after a check of what shipped. The plan is Cabinet officials from agency pages where the official speaks, then the 9 justices, speeches and remarks only. Opinions wait for a case record kind and a PDF reader. Executive orders as a record kind shipped with the Federal Register work. Depends on: none. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)
- R-58: More data sources, each from an official API or bulk file, with a terms and robots check as the first step of its PR. The order is value for effort: Congress.gov amendments, House NOMINATE scores, Census ACS district profiles, presidential vote share by district, FEC, LDA, and committees. Financial disclosures come last. [Candidate features](evidence-paths/features.md#new-evidence-sources) lists further sources to investigate. FEC money shows only as totals, with no donor names, because federal law limits reuse of donor names. Depends on: R-47. [Roster](architecture/roster.md#limits-and-planned-work)

- R-61: Finish executive action records with issue tags. The body text and the AI summary moved to R-68. The summary step and its publication are in code. The step is off by default. Issue tags need a design first, because these documents have no policy area. Depends on: none. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)
- R-62: Statements from official social accounts, such as Bluesky and YouTube. This needs a list of verified accounts and a new design. X and Meta stay out, because of cost and access limits. Depends on: R-17. [Collection](architecture/collection.md#limits-and-planned-work)

**Data and schemas**

- R-63: Move our own data out of JSON. Our data and our schemas are a first-class feature, so the database must enforce their rules. Decisions, revisions, and other data we define move to columns and child tables. Stored JSON stays only for text that must stay verbatim: the hashed configuration revision, the byte-compared document comparison, and raw model and collector input and output. Before a kind drops its JSON, a check rebuilds each revision from the new rows and compares it to the stored JSON. R-68 moves public readers and search ingestion to typed data, then removes public `release.json`. Future public MCP uses the public API. Each link between records becomes a real row with a foreign key, so that later insights can follow it. Depends on: none. [Architecture](architecture.md)
  - [ ] Move the other revision kinds, one PR for each. Executive action, document change, relationship, statement, brief, and action revisions are done in code through step 3. The production backfills and the JSON drops stay open
  - [ ] Move release entries to a table: one immutable row for each record revision, with a foreign key to that revision. `publication_record_states` gives the release membership. The code of `releases entries backfill <limit>` and `releases entries verify` is done. Open: the production runs, the foreign key from `publication_record_states`, and the move of the publish path off the manifest.
  - [ ] Stop reading the previous manifest in a publish. Retain private snapshots only while verification or recovery have callers.
  - [ ] Add a release-independent withdrawal tombstone ledger checked at `public_api` views and `ReleaseIngest`, so no rollback can resurrect a withdrawn record.
- R-27: Officials, sources, and discovery in tables. People data lives in tables, not in the configuration file. Sources and coverage move to tables too, and the owner commands and the worker write them. Check cadence then follows measured change rates, and discovery finds official sources. Plans: the git branches `survey-officials-in-tables` and `survey-source-discovery`. Depends on: none stated. [Roster](architecture/roster.md#officials-in-tables), [Configuration](architecture/configuration.md#limits-and-planned-work)
  - [ ] Officials in tables, PRs 4 to 8: bindings, pinned readers, live readers, coverage from `officeOf`, justices and gaps
  - [ ] Sources and coverage in tables
  - [ ] Check cadence from measured change rates
  - [ ] Discovery of official sources

**Operations**

- R-59: Deploy from GitHub Actions. First, the host and worker refuse to start on a missing or unknown `CIVIC_LENS_*` variable. Then a release job, gated by an owner click, reaches a forced command on the host through the tunnel. That command accepts only a full SHA on main, then updates and checks the live site. Depends on: none. [Host](architecture/host.md#limits-and-planned-work)
- R-60: Small open items. Fix the flaky tests. Check the Fischer source URL. Add Senate press email lists, if a terms check allows them. Obfuscate the email address on the About page. Add donations. Depends on: none. [Host](architecture/host.md#limits-and-planned-work)

### Next

- R-17: Congressional Record articles as a statement connector, Senate only at first. Each granule and speaker gives one candidate, and the extraction holds only the turns of that speaker. Code filters by class, title, covered member, and about 100 words. The public link is the GovInfo details page. A new quotation kind "Congressional Record text" is a contract change. Backfill to 2025-01-03. The PRs are collection to a ledger table with no model spend, then candidates, then the backfill, then removal of the 62 dead senate.gov listings and the 29 dead sources that fail a 7 day health check. Depends on: none. [Actions](architecture/actions.md#limits-and-planned-work)
- R-29: Check each policy area name against the Congress.gov API. Depends on: none stated. [Configuration](architecture/configuration.md#limits-and-planned-work)
- R-30: Fix the remaining format and parser limits that the federal coverage found. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)

### Later

- R-25: Public MCP through the public API over published data, and source health. Depends on: R-24. [Publication](architecture/publication.md#limits-and-planned-work)
- R-38: The shared language insight. Code finds the same or nearly the same sentences in releases from different offices, and publishes a method note. The maintainer reviews each finding before publication. Depends on: R-24. [Statements](architecture/statements.md#limits-and-planned-work)
- R-21: Process view of collection and AI stages from retained state. Depends on: R-24. [Host](architecture/host.md#limits-and-planned-work)
- R-18: An engine-health view, built with R-21. The spending total is in R-24. Depends on: R-24. [Collection](architecture/collection.md#limits-and-planned-work)
- R-13: Measure document change, then gate it. Check the House press index pages and about 300 press release URLs daily for 2 to 4 weeks. Count byte, text, and line changes for each source, and label about 100 changes by hand. Add rules for the churn found. Add a model tier only if the changes that pass the rules are too many for a solo maintainer to review. Depends on: R-24. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-22: Ordered row locks in the job store. Depends on: lock waits measured on a bills-only collection run. [Collection](architecture/collection.md#limits-and-planned-work)
- R-24: The long-term cloud move to a Hetzner CPX21 with containers, Terraform, CI, and live Auth0 verification. A Jetson runs temporary production with Docker Compose, and it serves releases through Caddy and a Cloudflare tunnel. Terraform state never goes to R2. The open host checklist items are its exit checks. Also stop the worker during a job and show that the deployed container recovers it. The search service runs as its own container on the same box. The deploy must set no provider-specific `Logging` levels for the search service, because such a rule overrides its request log filter. R-24 also holds a monthly restore test, worker service management, and the documented spending total. The owner withdraws with Take off site on the record editor, and the CLI stays. The runbook still holds a tested `releases publish --withdraw` command that runs on the cloud host. Revisit the coverage-file size limit only when the configured coverage requires it. Depends on: the remaining production-hosting checks in the linked runbook. [Host](architecture/host.md#limits-and-planned-work)
- R-26: Read-only local MCP inspection. Depends on: none stated. [Review](architecture/review.md#limits-and-planned-work)
- R-28: Datasets and fine-tuning from labels and review history. Depends on: none stated. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-31: Remove the redundant hunk and context copy in saved comparisons. Depends on: none stated. [Documents](architecture/documents.md#limits-and-planned-work)

## Pending decisions

Open pre-Evidence-Paths decisions:

- D-03: Global search and expanded reviewer source visibility. [Review](architecture/review.md#limits-and-planned-work)
- D-04: Bounded storage: store each distinct extraction text once, keyed by its hash. Keep raw capture bytes forever only when a gated change, draft, or release uses them or they are the latest version. Delete other bytes after a set period and keep the observation record. [Collection](architecture/collection.md#limits-and-planned-work)
- D-05: Classification ladder, only if the R-13 measurement shows that a model tier is needed: the uncertainty signal for each tier, where the escalation policy and thresholds live, and the stronger model for the reasoning tier. [Analysis](architecture/analysis.md#limits-and-planned-work)
- D-11: Drafting the past votes of a member who is added to tracking later. [Votes](architecture/votes.md#limits-and-planned-work)
- D-12: Content profiles for the 282 of 1,394 page, HTML, and feed sources that have none. [Documents](architecture/documents.md#limits-and-planned-work)
- D-13: A classifier benchmark for the statement issue gate, to run as soon as possible. The gate label store, the held-out split, the owner labeling page, and the label export are built ([Statements](architecture/statements.md#gate-labels)). Still to build: a Sonnet pre-label for releases outside the held-out set, and about 100 labels by hand, starting from the 48 measured ones. Then compare Haiku 5.5, Clef-flash, Clef-omni, Clef, a fine-tuned Clef, Strands Decider 2B, a fine-tuned ModernBERT, and a feature baseline. The issue list is configuration. A decision model takes the options in each request, so it needs no retraining when the list changes. A model that runs inside the worker (ONNX Runtime) needs a reviewed boundary change first. The fine-tuning needs the dataset rules of [R-28](#later) agreed first.
- D-16: Historical caucus for an independent first published after their roster term is unavailable. Published casts preserve a party known at publication. A later first publication may have only the source letter `I`, which cannot recover a former major-party caucus. Decide whether to collect historical terms or retain that limit. [Roster](architecture/roster.md#limits-and-planned-work)
- D-17: Whether the talk and action counts of a President include executive actions. Today they do not. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)

Evidence Paths decisions (accepted direction, 2026-11-24; move to feature docs as code implements each one):

- D-18 (Accepted): Error targets for the Phase 1 pilot and public trial: upper bound at most 5% for maintainer-accepted unsupported attributions in the private pilot, at most 2% in a public trial, at most 10% position assertions on insufficient cases in the pilot, and 0% tolerance for wrong-speaker errors. Because a 72-case pilot cannot certify 2%, ongoing public release audits certify public safety. [Pipeline evaluation](evidence-paths/pipeline.md#evaluation)
- D-19 (Accepted): Store "insufficient evidence" as a new `ReviewReasonCode.InsufficientEvidence` on `Reject`, and extend `RelationshipStaleness` to reopen only this code when the input fingerprint or evidence cutoff changes. Record `candidate_set_digest`, `retrieval_method_version`, and `as_of_utc` on the decision context. [Connection model](evidence-paths/pipeline.md#connection-model)
- D-20 (Accepted): For the private pilot, extend `RelationshipRevision` with an optional `TargetPassageSpan` (precedent: `ActionTextCitation`), a required `TargetRole` enum, and `MatchBasis`, without bumping `Publication.Contracts` until the pilot passes. Model a statement touching both a bill passage and a vote as two typed edges sharing one `StatementQuote` span. Defer a composite `ClaimAssessment` kind until multi-source outcome claims justify it. [Connection model](evidence-paths/pipeline.md#connection-model)
- D-21 (Accepted): Hold R-52 code approval of `StatementAboutVote` until the Phase 1 pilot, vote-role labels, and multi-speaker attribution controls pass. [Review](architecture/review.md#limits-and-planned-work)
- D-22 (Accepted): Export the frozen pilot release through `civic_lens_public` against `public_api` views in one `REPEATABLE READ` snapshot. Store the release export and pinned external files with `SHA256SUMS` outside Git (private object storage or a private GitHub release asset) and commit one `requirements.lock` for the experiment scripts. [Setup experiments](evidence-paths/pipeline.md#before-the-pilot-opens-its-holdout)
- D-23 (Accepted, solo maintainer): Draw holdout cases with a seeded deterministic rule from a pre-registered stratified frame committed in the pilot PR before running retrieval. Replace two-person double-labeling with a pre-registered rubric, blinded intra-rater re-labeling of at least 20 cases separated by at least 7 days (requiring intra-rater kappa of at least 0.80), deterministic code assertions, and an LLM devil's-advocate audit that flags rubric disputes without approving. [Pipeline evaluation](evidence-paths/pipeline.md#evaluation)
- D-24 (Accepted): Drop the independently deployed frontend shell as the proposed destination. Deliver the first interactive Evidence Path route inside `PublicApi` and `PublicWeb` using Razor SSR with server-rendered navigation and progressive JSON-backed panels. Reopen a separate shell only after a measured reader failure on the current stack. [Delivery](evidence-paths/delivery.md#one-route-before-a-stack-decision)

## Deferred

- News coverage in briefs: deferred until we measure how much of the real record a set of news feeds covers. Add coverage links and a gap signal only if that recall is high. Store feed entries only, never full articles.
- Congress.gov member records and a waiting window before sponsorship revisions: see [actions](architecture/actions.md#limits-and-planned-work) and [action decisions](architecture/actions.md#decisions).
- Self-hosted encoder models: see [analysis](architecture/analysis.md#limits-and-planned-work).
- Browser rendering, OCR, and cross-origin robots redirects: see [collection](architecture/collection.md#limits-and-planned-work) and [robots](architecture/collection.md#robots-and-pacing).
- Shared line-format rules for renderers: see [bills](architecture/bills.md#limits-and-planned-work).
- Versioned `release_issues` definition table migration: deferred because 192 rows across 16 definitions show no measured bottleneck. Fix missing legacy definitions directly in `PublicationCatalog`.
