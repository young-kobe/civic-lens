# Roadmap

This page holds the active plan and remaining backlog. Merged code, deployed behavior, and accepted reader flows are separate states. Remove completed work here and document implemented behavior in the feature references. Start at the [architecture lookup table](architecture.md#lookup-table).

## Current stage

Civic Lens is deployed and past its MVP and first deployment. Work now improves the running product through reviewed, tested iterations.

R-68 is the active priority. It closes the evidence, production, and reader obligations for the deployed redesign under the scope below. `PublicApi` serves every public page, and the publisher writes data only. The other items below remain the product backlog.

Current capabilities and ownership are in [architecture](architecture.md). Deployment, backups, recovery, and live checks are in [operations](operations.md).

## Work queue

Each item gives its outcome, dependencies, and design reference. Surface new architecture and public wording choices before implementation. Do not reopen settled choices.

### Active R-68 redesign

R-68 serves every public page from `PublicApi`, rendered from restricted `public_api` views. Typed published data owns each fact. The publisher writes data only, Caddy serves release photos, public HTML is cached per release, and ZIP fragments remain private. The [public site reference](architecture/public-site.md) owns implemented reader behavior.

The owner narrowed closeout on November 24, 2026. The remaining R-68 work is the list below. Reuse completed checks unless the relevant code, data or release selection changes. Incidental polish, return updates and PR639 remain outside this closeout. The earlier transfer of flat header/Menu, Bills/Votes primary-navigation placement and Home featured-vote visual acceptance remains in force; their function, accessibility, privacy and evidence obligations remain.

Completed evidence includes the bounded cold Home measurement, sampled member and issue readers, representative routes and data links, Maps, and the bounded shared-database workload. Historical bill recovery is complete. Flow 7 is closed using the existing native President navigation proof and the bounded live release-1470 count check: 607 documents for Donald Trump, with the four type counts matching published data. Further President helper work is stopped and moved to Evidence Paths.

- [ ] G3: store validated whole-roll-call totals on typed vote revisions through the existing stored-column and backfill workflow. Preserve tracked-member lists separately. Backfill and verify the selected Senate vote shows 52 Yea out of 100, rather than the tracked subtotal of 50 out of 98.
- [ ] G8: use each exact cited child revision's text origin on the server-rendered Brief page, preserving the API and release wire. Add the approved caption "Senate roll call {number}" using the existing official URL rule. Apply the three approved glossary corrections and Bill source link from the recorded wording review.
- [ ] G9: complete one real reader report. The owner submits the marked test; verify its stored identity and worker pull, then resolve it. The November 24 attempt returned the report-unavailable message, so diagnose that failure before another submission.
- [ ] G10: assess only the changed outputs against the retained owner criteria. Reuse the completed retained-surface review and unchanged reader evidence.
- [ ] G11: merge the reviewed changes with CI passing on their exact heads and perform the final rollout under the existing backup and deployment rules. Tell the owner immediately before rollout so the Anthropic account can be funded.
- [ ] G12: keep full rigor for final release files and rollback. Freeze publication, verify the original latest/active/served release and selected rollback release, activate and verify rollback, then restore the original release even on failure. Require latest, active and served to agree before restarting the worker. Reuse unchanged immutable checks and run only invalidated verifiers.
- [ ] G13: verify final transport, privacy and health after the last runtime change, and show the enabled model stages recover after funding. Run `check-live` last.

For the remaining gates, use one bounded live check per gate, one independent reviewer per change, and exact-head CI as the test record. Do not add pinned-hash proof files beyond the deploy script's output. Preserve existing evidence without building more proof machinery. G12 retains the full rollback and restore checks.

All eight pending owner decisions were accepted on November 24. Decisions 5 through 8 are covered by G3 and G8 above. The following accepted work is **moved to Evidence Paths** and must not block R-68:

- G7 typed Search term context, moved to Evidence Paths with decisions 2, 3 and 4: nullable `expectedBillKeyTermCount` is null for unknown, zero for none and one through five for an exact count; durable bounded Search progress serves complete results only and has a separate verifier; finite cancellation-aware `57P03` retry applies only to the initial Search writer connection open. Preserve the existing `search-term-context-local` worktree without resetting it.
- G6 fixed 40-case AI measurement, moved to Evidence Paths with decision 1: retain immutable per-case evidence IDs and actual per-case and per-pass prompt versions, within the existing 3M token cap. No paid sample is required for R-68 closeout.
- Further flow-7 President helper work, moved to Evidence Paths. The existing native navigation proof and bounded live count check satisfy this R-68 flow; no more helpers or helper reviews are required.

The model failures are an operational funding issue: the Anthropic account has no funds. They are not an outstanding design choice. No other remaining R-68 work needs paid calls. Production authority ends November 26 at 18:17 UTC; notify the owner early if G11 through G13 cannot finish within that window.

### Accepted plans

The owner accepted these designs. Each item holds its choices until the code and the feature doc replace them. Build each PR from main.

**Public site**

- R-47: Finish the remaining bill insights: sponsored bills that moved and bipartisan cosponsorship, with clear bases and method notes. Existing boards, badges, bill-status lines, and prior-Congress fallback are implemented. Their public API and server-rendered replacements belong to R-68. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
- R-48: What an official said and how they voted. The profile lists pairs, not totals: the quote with its date, then the cast with its question, date, and result. It shows no Yea or Nay totals and no talk against action quadrant, because a bare total lacks context. Depends on: R-47. [Publication](architecture/publication.md#limits-and-planned-work)
  - [ ] Said and voted pairs on the profile
- R-64: What officials did on an issue. Today a bill or vote links to an issue with no direction, so no official position can be derived. Add a row for what a bill does on an issue, with citations, and a row for what a Yea does on each vote, such as pass, amend, table, or proceed. Code then lists each official's casts with that evidence. The page shows no score and no "pro" or "anti" label. Effect on an issue starts with facts only: bill status, vote result, and whether the bill became law. Start with one tracked issue and measure accuracy and review load. The schema design goes to the owner first. Depends on: R-48, and the action redraft. [Publication](architecture/publication.md#limits-and-planned-work)
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
- R-58: More data sources, each from an official API or bulk file, with a terms and robots check as the first step of its PR. The order is value for effort: Congress.gov amendments, House NOMINATE scores, Census ACS district profiles, presidential vote share by district, FEC, LDA, and committees. Financial disclosures come last. FEC money shows only as totals, with no donor names, because federal law limits reuse of donor names. Depends on: R-47. [Roster](architecture/roster.md#limits-and-planned-work)

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

## Product direction

The audience is regular citizens who want transparency in U.S. national politics. State and local coverage are possible future extensions. Readers start with reviewed accounts and can inspect the evidence behind them. The editorial flow and the restrained monochrome identity are agreed.

Data is the product. The product is only as good as its data, so data and database schemas are a first class feature. Design them as deliberately as any public page. The goal is rich links between people, records, bills, votes, and issues, so that we can derive insights no single source shows. This may become a graph.

Design for scale. Each record carries rich detail, and coverage of officials, sources, and issues is broad. Automate every step that does not need human judgment.

Each page answers more questions than it opens (owner, 2026-11-06). Where a bill, a vote, an action, or a term is ambiguous or technical, the page gives an AI summary in plain words. The reader flow decides where a summary goes. Each summary carries its AI label, and code checks it against its source. Its prompt aims for grade 12; code rejects above 14. It explains the exact terms.

Derived insights are product goals. They make Civic Lens more than a connector to source APIs. Code computes each metric and publishes a method note. A person reviews any judgment before publication, and each insight shows its coverage limits.

1. Changed record: detect deleted press releases, rewritten quotations, and positions removed from issue pages. Show both versions and their timing next to votes and events. This needs deletion detection and the document-change gate.
2. Shared language: find the same or nearly the same sentences in releases from different offices. Show coordinated messaging, how it spreads, and who uses it first.

## Deferred

- News coverage in briefs: deferred until we measure how much of the real record a set of news feeds covers. Add coverage links and a gap signal only if that recall is high. Store feed entries only, never full articles.
- Congress.gov member records and a waiting window before sponsorship revisions: see [actions](architecture/actions.md#limits-and-planned-work) and [action decisions](architecture/actions.md#decisions).
- Self-hosted encoder models: see [analysis](architecture/analysis.md#limits-and-planned-work).
- Browser rendering, OCR, and cross-origin robots redirects: see [collection](architecture/collection.md#limits-and-planned-work) and [robots](architecture/collection.md#robots-and-pacing).
- Shared line-format rules for renderers: see [bills](architecture/bills.md#limits-and-planned-work).
