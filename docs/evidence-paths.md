# Evidence Paths

Evidence Paths is the Civic Lens plan. It holds the direction, the phases, the active work, the accepted backlog, and the pending decisions. Merged code, deployed behavior, and accepted reader flows are separate states. Remove completed work here and document implemented behavior in the feature references. Start at the [architecture lookup table](architecture.md#lookup-table).

## Direction

Civic Lens should let a citizen follow a public claim through a decision to a measured outcome. Start with a question that matters to the reader. Give each answer a date, an accountable actor, and a short path to the original evidence.

One [relationship pipeline](evidence-paths/pipeline.md) finds connections across many dimensions of the data. It aggregates them and shows them in an informative, interactive way. The aim is to make records that are hard to find or hard to connect visible to a regular reader. The site shows what the records say and how they connect. It never states a conclusion. The reader draws the conclusion. The same rules apply to every official, issue and dimension, so no single case or topic shapes the output. Single cases, such as the VA claims backlog, test the pipeline. They are not the product.

The audience is regular citizens who want transparency in U.S. national politics. State and local coverage are possible future extensions. Readers start with reviewed accounts and can inspect the evidence behind them. The editorial flow and the restrained monochrome identity are agreed.

Data is the product. The product is only as good as its data, so data and database schemas are a first class feature. Design them as deliberately as any public page. The goal is rich links between people, records, bills, votes, issues and outcomes, so that we can derive insights no single source shows.

Design for scale. Each record carries rich detail, and coverage of officials, sources, and issues is broad. Automate every step that does not need human judgment.

Each page answers more questions than it opens (owner, 2026-11-06). Where a bill, a vote, an action, or a term is ambiguous or technical, the page gives an AI summary in plain words. The reader flow decides where a summary goes. Each summary carries its AI label, and code checks it against its source. Its prompt aims for grade 12; code rejects above 14. It explains the exact terms.

Derived insights are product goals. They make Civic Lens more than a connector to source APIs. Code computes each metric and publishes a method note. A person reviews any judgment before publication, and each insight shows its coverage limits. [Candidate features](evidence-paths/features.md) lists the insights and features to investigate. Investigation can remove any of them.

## Current stage

Civic Lens is deployed and past its MVP and first deployment. Work now improves the running product through reviewed, tested iterations. Current capabilities and ownership are in [architecture](architecture.md). Deployment, backups, recovery, and live checks are in [operations](operations.md).

R-68 is the active priority. It closes the evidence, production, and reader obligations for the deployed redesign under the [approved scope](#active-r-68-redesign). `PublicApi` serves every public page, and the publisher writes data only. The private evidence pilot can run independently on frozen data. The other phases are the proposed build-out. Items in the [work queue](#work-queue) keep their dependencies. Move accepted behavior into its feature reference as each phase ships.

## Phases

| Phase | Deliverable | Evidence for moving forward |
|---|---|---|
| 0, across all phases. [Relationship pipeline](evidence-paths/pipeline.md) | One connection model, ordered candidate sources, review and statistical gates, and display rules | Each displayed connection states its type, role and basis, binds exact evidence, and survives withdrawal on every surface. |
| 1. [Claims and evidence](evidence-paths/claims.md) | A reproducible outcome audit and a multi-issue test of statement, provision and vote links | A reviewer can reconstruct each finding from exact source versions and explain the remaining gaps. |
| 1, from the first case. [Evaluation and observability](evidence-paths/evaluation.md) | A private run comparison, a labeled holdout and a public methods summary | Show which stage improved, the effect on critical errors and coverage, and the cost per accepted finding. |
| 2. [Graph and statistical research](evidence-paths/graph.md) | Typed paths across records and a bounded comparison of retrieval methods | Each additional correct link survives review and leads to cited evidence. Measure the cost of maintaining the projection. |
| 3. [Reader experience](evidence-paths/reader.md) | Issue timelines, a changing homepage and a collapsible left navigation | New readers can explain what happened, find their member's part, and distinguish a verified fact from an open question. |
| 4. [Frontend delivery](evidence-paths/delivery.md) | One independently deployable issue route using the published API | A shell survives an unavailable API. Deep links, withdrawals, privacy and release consistency pass the route trial. |

The [evidence interface](evidence-paths/evidence-interface.md) shows which reader questions can use existing joins and which need additional classified evidence. The next phase will keep JSON authoring, store each public issue definition once per version and bind each release to its membership, definition versions and counts. The private pilot can use the current published views before that migration.

## First assignment

First run the [setup experiments](evidence-paths/pipeline.md#before-the-pilot-opens-its-holdout), so that the pilot has frozen inputs and fixed scoring. Then start a fresh branch from current main. Read the [claims workflow](evidence-paths/claims.md#proposed-workflow-and-ownership), [relationship review](architecture/statements.md#relationship-drafting), [evaluation plan](evidence-paths/evaluation.md) and [trial results](evidence-paths/experiments/evidence-trials/results.md). Use a frozen published release and pinned source documents in development.

Build one private review flow. The [local graph prototype and reader counterpart](evidence-paths/graph.md#a-local-graph-in-relationship-review) demonstrate the same sources in map and list views. Put the attributed quotation and its date beside the exact bill edition and passage, the roll-call question, and the member's recorded cast when one exists. Let the reviewer accept or reject the proposed link or record insufficient evidence. Bind that decision to the evidence revisions so a second reviewer can reproduce it. Start with the existing relationship-drafting and review handlers. Bring any required schema or public-contract choice to the owner using those handlers as the precedent.

Demonstrate the flow on a supported link and a tempting wrong link, then test on unseen bill families sized under the [pipeline evaluation](evidence-paths/pipeline.md#evaluation). Capture complete evidence sets in the first five candidates, unsupported attributions and review time when a reviewer is available. Keep cases without a sufficient vote in the denominator for evaluating abstention. The two food-assistance positives in the trial share a bill and provision. They are now development examples. Reserve fresh cases for the next transfer test. The pipeline evaluation lists the other development families.

Hand back the private workflow in its PR with a replay command, pinned inputs, baseline and candidate counts, and one decision a reviewer can reconstruct. List unresolved cases and the missing evidence for each. The pilot can finish with an inconclusive result. The product owner uses the [evaluation criteria](evidence-paths/evaluation.md#pilot-decision-and-feedback) to choose whether to retain the baseline, fund a specific follow-up or scope a public trial. A later, separately scoped [public issue-route trial](evidence-paths/delivery.md#one-route-before-a-stack-decision) can use a reviewed finding to test a direct link, API unavailability and evidence withdrawal before choosing a frontend delivery approach. The reader phase owns [navigation and homepage selection](evidence-paths/reader.md). The [mockup coverage table](evidence-paths/reader.md#what-the-mockups-demonstrate) identifies the interactions a person can replay and the design decisions that need readers or a release-backed route.

## Experiments

| Experiment | Result and repeatable materials |
|---|---|
| [AI and statistics on existing evidence](evidence-paths/experiments/applied-methods.md) | Tested agreement between two retrievers, hierarchical clustering of 876 bill sections, and time to a committee report for 64 bills. The [research priorities](evidence-paths/graph.md#which-ai-or-statistical-study-comes-next) tie each next comparison to a product decision. |
| [Versioned issue definitions](evidence-paths/evidence-interface.md#can-a-versioned-table-replace-the-repeated-definitions) | Twelve retained releases contained 192 issue rows and 16 distinct public definitions. A separate synthetic rollback check preserved historical wording, counts and membership. |
| [Reuse of issue assignments across features](evidence-paths/evidence-interface.md#offline-interface-check) | Three House votes supported two reader views. Adding a poll supplied two contextual matches and zero eligible directional comparisons. [Runnable SQL](evidence-paths/experiments/reuse-issue-facts.sql). |
| [Five linked-evidence trials](evidence-paths/experiments/evidence-trials/results.md) | Tested quote retrieval, provision continuity, charging delivery and coalition recurrence. Exercised all reader form assignments at two widths. Human comprehension remains unmeasured. |
| [Published graph on two House bills](evidence-paths/experiments/published-graph.md) | In release 1416, 338 sponsorships produced 56,281 member pairs. The same common backer appeared in SQL and NetworkX. [Query](evidence-paths/experiments/published-graph.sql) and [analysis](evidence-paths/experiments/project-sponsors.py). |
| [Clustering bill provisions across bills](evidence-paths/experiments/section-clusters.md) | Clustered 876 sections in eight editions. Closely worded insulin provisions grouped together in all nine standard runs. A related refueling grant and credit stayed apart. |
| [Amendment or motion versus final passage](evidence-paths/experiments/paired-votes.md) | Of senators casting Yea or Nay on both questions, 7 of 100 cast different votes in the insulin case and 9 of 95 in the immigration case. |
| [Issue mapping across legislative subjects](evidence-paths/experiments/issue-mapping.md) | Tested statement-to-provision retrieval and topic candidates on four bill editions. Verified four roll-call questions and seven selected casts. |
| [Veterans claims backlog at two cutoffs](evidence-paths/experiments/va-backlog.md) | VA's inauguration-day count gives a 72.07% decline by Oct 10 and 69.95% by Nov 7. Both weekly baselines exceed 70% by Oct 10 and fall short of it by Nov 7. [Workbook calculation](evidence-paths/experiments/va-backlog.py). |

## Which research would change the next build?

The [five completed trials](evidence-paths/experiments/evidence-trials/results.md) establish the starting point. Only the first row belongs to the private pilot. Reopen the other questions when the named input is available and the result would change a product decision.

| Question | What the completed trial established | Next decision and evidence needed |
|---|---|---|
| Can a reviewer connect a quote to the right decision? | Explicit links recovered 4 of 4 selected provision-and-vote pairs in the first five. Every method returned candidates for all 3 cases with insufficient evidence. | Compare against lexical search on an untouched bill family, with the same five slots and cutoff. Score candidate coverage and unjustified attributions separately. The owner chooses a method after seeing both. |
| Did a provision survive into the next edition? | Text matching got 9 of 10 selected transitions right. One false match changed the legal effect. | Before offering a change timeline, test fresh editions with a changed mechanism or a dropped provision. Keep uncertain transitions open for review. |
| Did a funded project reach delivery? | The Deerfield case linked an obligation and a reported opening. The opening report omitted the award ID. | Test a second recipient with a dated site-level completion or expenditure record. Until then, show each verified stage and its source. |
| Do shared sponsors form a recurring coalition? | Removing the most widely backed early bill reduced later overlap from 22 of 58 repeated pairs to 1 of 15. | Revisit a coalition feature with wider time windows and a comparison accounting for party or committee. Keep the underlying bill links available. |
| Do readers understand the evidence? | The two-layout instrument ran at both widths. No first-time readers took part. | When readers are available, score their explanations and source use. Test the navigation structure separately by asking readers to find a destination. [Reader trial](evidence-paths/reader.md#decision-test). |

## Decisions to take in order

1. Start the private quote-to-vote review. Use the existing revision and review owners. First answer [D-18 to D-23](#pending-decisions). Before tuning, the product owner sets error tolerance and minimum coverage, and the pilot agent freezes the comparison. The catalog migration can proceed separately after its release-equivalence checks are ready. [Pilot evaluation](evidence-paths/evaluation.md#pilot-decision-and-feedback) and [catalog migration](evidence-paths/evidence-interface.md#can-a-versioned-table-replace-the-repeated-definitions).
2. Use PostgreSQL joins for known paths. Commission a graph or model comparison when a named reader question and a measured weakness justify it. Keep the simpler method if results are inconclusive. [Graph research](evidence-paths/graph.md).
3. Use first-time readers to choose the navigation and test comprehension. Use published releases to replay featured-item selection. Each study answers a different question. [Reader decisions](evidence-paths/reader.md#decision-test).
4. Scope one public issue route around an eligible reviewed finding. Choose its runtime after testing API outages, revision consistency, withdrawals, accessibility and rollback. The owner decides wider rollout after seeing the comparison with the deployed route. [Delivery trial](evidence-paths/delivery.md#acceptance-trial).

## Work queue

Each item gives its outcome, dependencies, and design reference. Surface new architecture and public wording choices before implementation. Do not reopen settled choices.

### Active R-68 redesign

R-68 serves every public page from `PublicApi`, rendered from restricted `public_api` views. Typed tables own each fact. JSON files stop carrying data into the public serving path. Cloudflare caches public HTML for each release. ZIP results remain private fragments. The [public site reference](architecture/public-site.md#decisions) describes the deployed design and the accepted reader flows.

The restricted views, typed read APIs, shared Razor library, page cache, Home preview, and private ZIP cards and shared votes are merged. APIs also serve CRS bodies, bill histories, statement and document-change evidence, briefs, withdrawals, official figures, and party agreement. Their exact contracts are in [search](architecture/search.md#api) and [publication](architecture/publication.md#public-api-views). A merged API does not complete its reader page or live rollout.

PublicApi serves every public page at its public path since cutover. Caddy serves only the photos from the release. The member strip and both private ZIP layouts are deployed. Production correction history, tally, line-up, vote-bill, and official record-count checks have passed. Repeated aggregation and late filtering account for part of the measured ZIP query cost. Cold reads and larger selections still need measurement after the query changes reach production.

Historical bill-action recovery is complete in production. Final checks of release files and retained rollback releases remain open.

The owner approved the R-68 closure scope on November 19, 2026. The next UI phase owns the outstanding visual decisions for the flat header and native Menu, the placement of Bills and Votes in primary navigation, and the featured-vote presentation on Home. Only outstanding visual acceptance for those three choices is superseded. R-68 retains public routes and permalinks, the serving stack, navigation and Home functionality, keyboard and mobile use, accessibility, private ZIP handling, evidence accuracy, and release consistency on those surfaces.

Remaining route work. Each slice includes the data, source evidence, labels, and checks its reader flow needs.

- [ ] Complete the Maps reader-flow, keyboard, touch and production checks under the full audit below. The interactive Maps page and shared renderer cover term ends, issue activity and attendance. Matching House shapes cover the 118th and 119th Congresses; other Congresses retain tiles and tables. Accepted dates, holders, bases and unavailable states are in [public site](architecture/public-site.md#interactive-maps).
- [ ] Add the document changes list once document changes are published.

Data work below remains part of R-68. Follow the production commands and verification rules in [operations](operations.md#production-runbook).

- [ ] Finish the executive document summary work. The step, publication, label, and page slot are built and off by default. A budgeted sample was measured. Check its results against the current prompt, the recorded owner decision, and actual activation before closing this work.

AI text and vocabulary remain required work, not optional follow-ups.

- [ ] Keep the page text gate whole as R-68 adds pages. Statement, Home, vote and bill summaries, bill why-it-matters prose, document-change prose, executive-action summaries, relationship reasons, and weekly brief paragraphs have exact-name term popovers. Bill prose prefers its cited local definitions. Remaining: AI-text checks beyond the draft check, and term controls beside linked headlines and search snippets.
- [ ] Check each changed prompt against its fixed-record measurement, evidence, and required sample approval. Reuse completed measurements for their exact prompt versions. New paid runs need an explicit budget. Keep model activation and historical backfills separate from routine implementation and deployment permission. Reuse the established [R-67 measurement flow](architecture/actions.md#limits-and-planned-work).
- [ ] Verify up to five bill key terms, defined only by exact quotations from the definitions section. Code finds the section, checks each quote, stores the terms, serves them in the bill detail API, and reports them in `actions text measure`, all behind `CIVIC_LENS_ACTION_DRAFTING_KEY_TERMS`, which is off by default. A combined bill-text and key-term sample was measured. Confirm the prompt version and activation against the recorded owner decisions. Executive summaries describe what the document states, never its motive. Amendment-purpose summaries wait for amendment data under R-58.

Return-visit features are parked and are not part of R-68.

Complete these gates before closing R-68.

- [ ] Reconcile the component audit, M1 through M10, DR1 through DR10, all 18 reader flows, and all seven owner criteria against the approved scope. Split mixed audit items so each retained requirement has evidence and each transferred visual choice has an explicit disposition. Use the [public site reference](architecture/public-site.md) and the existing local audit evidence. Check the executive body experience against the accepted design, source flow, approved glossary text, "Get this data" links on published-data tables, grade, accessibility, keyboard and mobile use, privacy, and the paged source APIs. Owner criteria also require evidence beyond unit tests.
- [ ] Measure cold reads, traffic, query work, and worker effects on the shared database. Close the open Home cold-read timeout and verify larger ZIP selections. Warm generated-data probes do not establish cold production performance.
- [ ] Complete remaining R-68 deployments under the [runbook](operations.md#production-runbook). Require matching artifacts and images, fresh backups, functional and privacy checks, and worker health. After the final rollout and conversions, verify corrections, tallies, and line-ups for the selected release and retained rollback releases. Verify Cloudflare compression and caching, with private fragments excluded from shared caches. Run the final live check last.
- [ ] Run the final source, data, privacy, reader-flow, and design audit for the retained scope. Remove unused code and temporary migration rules. Record the deployed commit, verified release and retained rollback releases, recovery endpoint, and evidence for the retained gates. Keep R-68 open until the retained gates pass, then reconcile this plan and the affected architecture references.

### Accepted plans

The owner accepted these designs. Each item holds its choices until the code and the feature doc replace them. Build each PR from main.

**Public site**

- R-47: Finish the remaining bill insights: sponsored bills that moved and bipartisan cosponsorship, with clear bases and method notes. Existing boards, badges, bill-status lines, and prior-Congress fallback are implemented. Their public API and server-rendered replacements belong to R-68. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
- R-48: What an official said and how they voted. The profile lists pairs, not totals: the quote with its date, then the cast with its question, date, and result. It shows no Yea or Nay totals and no talk against action quadrant, because a bare total lacks context. The [Phase 1 pilot](#first-assignment) decides the evidence roles of each pair. Depends on: R-47. [Publication](architecture/publication.md#limits-and-planned-work)
  - [ ] Said and voted pairs on the profile
- R-64: What officials did on an issue. Today a bill or vote links to an issue with no direction, so no official position can be derived. Add a row for what a bill does on an issue, with citations, and a row for what a Yea does on each vote, such as pass, amend, table, or proceed. Code then lists each official's casts with that evidence. The page shows no score and no "pro" or "anti" label. Effect on an issue starts with facts only: bill status, vote result, and whether the bill became law. Start with one tracked issue and measure accuracy and review load. The schema design goes to the owner first, using the [connection model](evidence-paths/pipeline.md#connection-model). Depends on: R-48, and the action redraft. [Publication](architecture/publication.md#limits-and-planned-work)
- R-69: Representation audit. Show how an official acts for their constituents, with the evidence side by side and its error named. Civic Lens never publishes a composite representation score or grade, its own or a scorecard's. Each feature has a method note. The research is on branch `survey-representation-data`. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
  - [ ] How it passed: each bill and nomination shows roll call, voice vote, or unanimous consent, so readers see when no member's position is on record
  - [ ] Bill fate: the profile shows how far each sponsored or cosponsored bill got and when the official joined it, beside the chamber base rate, with no rank
  - [ ] District context: Census district facts with their margins of error beside votes on matching issues. The owner chooses where district facts are stored before this PR
  - [ ] Ideology and district: the NOMINATE score beside the district presidential margin, with a reviewed method note
  - [ ] Constituent opinion: survey estimates with error bars, only for an issue with a published estimate whose terms allow reuse, after a person reviews the method note

**Review and approval**

- R-51: Delete the gate-label pages after D-13. The combined review queue, Take off site, and reports in the editor are implemented. Depends on: D-13. [Review](architecture/review.md#limits-and-planned-work)
- R-65: Move to the next queue item after a review decision. Keep typed text after a failed approval and preserve every approval check. The shared editor and Save and approve transaction are implemented. Depends on: R-51 take off site. [Review](architecture/review.md#limits-and-planned-work)
- R-52: Approve factual links by code. The types are "statement about a vote" and "describes own bill". The types "states position on" and "claims credit for" stay with a person. Depends on: R-51. [Review](architecture/review.md#limits-and-planned-work)
  - [ ] Code approval of factual links, and new public wording
- R-54: Finish bill issue narrowing. The model gets the Congress.gov legislative subjects when a bill has them, and the redraft command exists. Still open: run the subjects backfill and the redraft command on the live site. Then check on a sample that a bill with no fitting issue publishes with no issue. Depends on: none. [Actions](architecture/actions.md#limits-and-planned-work)
  - [ ] Run the subjects backfill and the redraft command
  - [ ] Draft the action backlog, and spot check the issue tags after it

- R-66: Gate model output on confidence. Models give a confidence score. A low score sends the record to the one page review, where a person fixes it or tells the model what to fix. Set the threshold from measured data and a person-labeled set. Depends on: R-65. [Review](architecture/review.md#limits-and-planned-work)
  - [ ] Agree the design
  - [ ] Build the gate and the model fix
- R-67: Measure each prompt. Each prompt version gets a live, budgeted, opt-in measure on fixed bills, and the numbers go on the PR. Use the cheapest setting that holds accuracy. The order is vote, statement draft and gate, document change, relationship, then brief. Depends on: none. [Analysis](architecture/analysis.md#limits-and-planned-work)
  - [ ] Agree one opt-in eval command. Later prompt measures extend `actions text measure` or replace it with one command
  - [ ] Measure the other prompts, one PR for each

**Coverage**

- R-55: Stop losing feed items. Items past `maxJobs` are dropped for good today, and every member feed uses `maxJobs` 2. Survey a drain of deferred candidates, then build it. Depends on: none. [Collection](architecture/collection.md#limits-and-planned-work)
  - [ ] Survey and build a drain of deferred candidates
- R-56: Publish source coverage. Add a coverage field for each official and a "sources we could read" panel, so uneven coverage is visible. Depends on: none. [Publication](architecture/publication.md#limits-and-planned-work)
- R-57: Finish the executive and judicial sources, after a check of what shipped. The plan is Cabinet officials from agency pages where the official speaks, then the 9 justices, speeches and remarks only. Opinions wait for a case record kind and a PDF reader. Executive orders as a record kind shipped with the Federal Register work. Depends on: none. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)
- R-58: More data sources, each from an official API or bulk file, with a terms and robots check as the first step of its PR. The order is value for effort: Congress.gov amendments, House NOMINATE scores, Census ACS district profiles, presidential vote share by district, FEC, LDA, and committees. Financial disclosures come last. [Candidate features](evidence-paths/features.md#new-evidence-sources) lists further sources to investigate. FEC money shows only as totals, with no donor names, because federal law limits reuse of donor names. Depends on: R-47. [Roster](architecture/roster.md#limits-and-planned-work)

- R-61: Finish executive action records with issue tags. The body text and the AI summary moved to R-68. The summary step and its publication are in code. The step is off by default. Issue tags need a design first, because these documents have no policy area. Depends on: none. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)
- R-62: Statements from official social accounts, such as Bluesky and YouTube. This needs a list of verified accounts and a new design. X and Meta stay out, because of cost and access limits. Depends on: R-17. [Collection](architecture/collection.md#limits-and-planned-work)

**Data and schemas**

- R-63: Move our own data out of JSON. Our data and our schemas are a first class feature, so the database must enforce their rules. Decisions, revisions, and other data we define move to columns and child tables. Stored JSON stays only for text that must stay verbatim: the hashed configuration revision, the byte-compared document comparison, and raw model and collector input and output. Before a kind drops its JSON, a check rebuilds each revision from the new rows and compares it to the stored JSON. R-68 moves public readers and search ingestion to typed data, then removes public `release.json`. Future public MCP uses the public API. Each link between records becomes a real row with a foreign key, so that later insights can follow it. Depends on: none. [Architecture](architecture.md)
  - [ ] Move the other revision kinds, one PR for each. Executive action, document change, relationship, statement, brief, and action revisions are done in code through step 3. The production backfills and the JSON drops stay open
  - [ ] Move release entries to a table: one immutable row for each record revision, with a foreign key to that revision. `publication_record_states` gives the release membership. The code of `releases entries backfill <limit>` and `releases entries verify` is done. Open: the production runs, the foreign key from `publication_record_states`, and the move of the publish path off the manifest.
  - [ ] Stop reading the previous manifest in a publish. Retain private snapshots only while verification or recovery have callers.
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
- R-38: The shared language insight. Code finds the same or nearly the same sentences in releases from different offices, and publishes a method note. A person reviews each finding before publication. Depends on: R-24. [Statements](architecture/statements.md#limits-and-planned-work)
- R-21: Process view of collection and AI stages from retained state. Depends on: R-24. [Host](architecture/host.md#limits-and-planned-work)
- R-18: An engine-health view, built with R-21. The spending total is in R-24. Depends on: R-24. [Collection](architecture/collection.md#limits-and-planned-work)
- R-13: Measure document change, then gate it. Check the House press index pages and about 300 press release URLs daily for 2 to 4 weeks. Count byte, text, and line changes for each source, and label about 100 changes by hand. Add rules for the churn found. Add a model tier only if the changes that pass the rules are too many for people to review. Depends on: R-24. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-22: Ordered row locks in the job store. Depends on: lock waits measured on a bills-only collection run. [Collection](architecture/collection.md#limits-and-planned-work)
- R-24: The long-term cloud move to a Hetzner CPX21 with containers, Terraform, CI, and live Auth0 verification. A Jetson runs temporary production with Docker Compose, and it serves releases through Caddy and a Cloudflare tunnel. Terraform state never goes to R2. The open host checklist items are its exit checks. Also stop the worker during a job and show that the deployed container recovers it. The search service runs as its own container on the same box. The deploy must set no provider-specific `Logging` levels for the search service, because such a rule overrides its request log filter. R-24 also holds a monthly restore test, worker service management, and the documented spending total. The owner withdraws with Take off site on the record editor, and the CLI stays. The runbook still holds a tested `releases publish --withdraw` command that runs on the cloud host. Revisit the coverage-file size limit only when the configured coverage requires it. Depends on: the remaining production-hosting checks in the linked runbook. [Host](architecture/host.md#limits-and-planned-work)
- R-26: Read-only local MCP inspection. Depends on: none stated. [Review](architecture/review.md#limits-and-planned-work)
- R-28: Datasets and fine-tuning from labels and review history. Depends on: none stated. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-31: Remove the redundant hunk and context copy in saved comparisons. Depends on: none stated. [Documents](architecture/documents.md#limits-and-planned-work)

## Pending decisions

- D-03: Global search and expanded reviewer source visibility. [Review](architecture/review.md#limits-and-planned-work)
- D-04: Bounded storage: store each distinct extraction text once, keyed by its hash. Keep raw capture bytes forever only when a gated change, draft, or release uses them or they are the latest version. Delete other bytes after a set period and keep the observation record. [Collection](architecture/collection.md#limits-and-planned-work)
- D-05: Classification ladder, only if the R-13 measurement shows that a model tier is needed: the uncertainty signal for each tier, where the escalation policy and thresholds live, and the stronger model for the reasoning tier. [Analysis](architecture/analysis.md#limits-and-planned-work)
- D-11: Drafting the past votes of a member who is added to tracking later. [Votes](architecture/votes.md#limits-and-planned-work)
- D-12: Content profiles for the 282 of 1,394 page, HTML, and feed sources that have none. [Documents](architecture/documents.md#limits-and-planned-work)
- D-13: A classifier benchmark for the statement issue gate, to run as soon as possible. The gate label store, the held-out split, the owner labeling page, and the label export are built ([Statements](architecture/statements.md#gate-labels)). Still to build: a Sonnet pre-label for releases outside the held-out set, and about 100 labels by hand, starting from the 48 measured ones. Then compare Haiku 5.5, Clef-flash, Clef-omni, Clef, a fine-tuned Clef, Strands Decider 2B, a fine-tuned ModernBERT, and a feature baseline. The issue list is configuration. A decision model takes the options in each request, so it needs no retraining when the list changes. A model that runs inside the worker (ONNX Runtime) needs a reviewed boundary change first. The fine-tuning needs the dataset rules of [R-28](#later) agreed first.
- D-16: Historical caucus for an independent first published after their roster term is unavailable. Published casts preserve a party known at publication. A later first publication may have only the source letter `I`, which cannot recover a former major-party caucus. Decide whether to collect historical terms or retain that limit. [Roster](architecture/roster.md#limits-and-planned-work)
- D-17: Whether the talk and action counts of a President include executive actions. Today they do not. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)


- D-18: Error targets for the Phase 1 pilot and a public trial, set before tuning. The review proposes an upper bound of 5% for reviewer-accepted unsupported attributions in the private pilot and 2% in a public trial. [Pipeline evaluation](evidence-paths/pipeline.md#evaluation)
- D-19: Where "insufficient evidence" lives: a new relationship reject reason that reopens when the evidence fingerprint changes, or a request for changes. [Connection model](evidence-paths/pipeline.md#connection-model)
- D-20: How a relationship cites a bill passage: an optional target span on the relationship revision, after the action text citation precedent, a choice of existing action sentences, or a new assessment kind. [Connection model](evidence-paths/pipeline.md#connection-model)
- D-21: Whether R-52 code approval of "statement about a vote" waits for the Phase 1 pilot result, or the pilot excludes that type. [Review](architecture/review.md#limits-and-planned-work)
- D-22: Frozen pilot inputs: whether the release export reads the public views in one snapshot or the operational tables, and where the input bundle lives outside Git. [Setup experiments](evidence-paths/pipeline.md#before-the-pilot-opens-its-holdout)
- D-23: Who draws the holdout cases: a person who did not build the method, or a seeded rule recorded in the pilot PR. [Pipeline evaluation](evidence-paths/pipeline.md#evaluation)
- D-24: Whether the delivery phase keeps an independently deployed shell as its proposed destination, or first proves a reader need that the server-rendered pages cannot meet. [Delivery](evidence-paths/delivery.md#one-route-before-a-stack-decision)

## Deferred

- News coverage in briefs: deferred until we measure how much of the real record a set of news feeds covers. Add coverage links and a gap signal only if that recall is high. Store feed entries only, never full articles.
- Congress.gov member records and a waiting window before sponsorship revisions: see [actions](architecture/actions.md#limits-and-planned-work) and [action decisions](architecture/actions.md#decisions).
- Self-hosted encoder models: see [analysis](architecture/analysis.md#limits-and-planned-work).
- Browser rendering, OCR, and cross-origin robots redirects: see [collection](architecture/collection.md#limits-and-planned-work) and [robots](architecture/collection.md#robots-and-pacing).
- Shared line-format rules for renderers: see [bills](architecture/bills.md#limits-and-planned-work).
