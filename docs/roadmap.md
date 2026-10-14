# Remaining implementation

## MVP goal

The MVP delivers the full product proposition: officials' statements, authoritative actions, substantive document changes, and evidence-backed weekly briefs. A document-change-only release is an intermediate milestone, not the MVP.

The initial audience is regular citizens seeking transparency in U.S. national politics. Follow the [approved editorial experience](architecture.md#approved-editorial-experience-target): readable reviewed accounts lead to inspectable evidence, and private review presents the proposed public record beside its sources. State and local coverage are possible future extensions. The editorial flow and restrained monochrome identity are agreed; detailed public layouts remain to be built.

Initial coverage may be small, but all four capabilities must work end to end: collect evidence, retain exact inspectable provenance, review proposed output, and publish a static release that remains usable during an operational application outage. Human approval must bind to the exact evidence version; model output never constitutes approval. Statements and actions must support reviewed relationships and timelines, document changes must distinguish substantive edits from navigation churn and parser upgrades, and weekly briefs must use verified citations and explicit coverage cutoffs.

Public search is an MVP requirement. Readers must be able to search all published records from the public pages. An easy interface must let them filter by record kind, official, issue, policy area, date range, and source. Search reads the published release only, so it must keep working while the operational application is down. The search design is not decided. Keep it in mind when designing record contracts, so each record carries the fields that filters need.

AI drafting and classification are MVP requirements. AI drafts document-change accounts, statement and action records, and weekly briefs. Reviewers edit, approve, and publish these drafts. A classification gate keeps navigation churn and other nonsubstantive changes out of review.

Design for scale. The product needs rich detail in each record and broad coverage of officials, sources, and issues. Automate every step that does not need human judgment. The worker drafts each change that passes the gate; no operator starts it. Human approval remains the only manual step before publication, so review must handle a large volume (see [Reviewer and public experience](#reviewer-and-public-experience)).

The phases below sequence delivery toward this goal. Completing collection or document changes alone does not satisfy it.

Phase 0, the watched-page tracer, version 9 collection contracts that read stored version 7 and 9 receipts, Core collection-import rules, the Application collection/import handler, the Postgres adapter with its initial migration, CLI database setup and collection/import, durable receipt handoff/replay, managed jobs with fenced ownership, aggregate budgets, retries, cancellation, and origin pacing/backoff, and a separately launched sequential queue worker are implemented. RSS/Atom and scoped HTML discovery with explicit bounded article admission are implemented. HTML discovery uses tolerant recovery; `capture-text-v2` extracts the retained Warren article captures with the configured profile. The authenticated review workspace includes owner-only source collection, cancellation, admission, extraction, comparison, and article recollection actions. Bounded robots handling and durable crawl-delay pacing are implemented. Configuration v2 dated coverage/names and immutable configuration revisions bound to managed jobs are also implemented. Current live Auth0 login remains unverified. The following is remaining planned work, not available functionality. Remove completed work and update the current architecture/operations references; Git preserves previous plans.

## Next delivery milestone

The first document-change review workflow is implemented: browse saved comparisons, create drafts, inspect preview/evidence, save immutable revisions, and record attributed approval, requested changes, or withdrawal. The owner can publish approved revisions as an immutable static release from the workspace or the CLI, with atomic activation and CLI rollback. The worker now writes an AI draft of each document-change account ([architecture](architecture.md#ai-drafting-of-document-change-accounts-implemented)). It also gates statement candidates and writes an AI draft of each statement that passes ([architecture](architecture.md#statement-drafting-implemented)). The statement review backend and pages are implemented, and approved statements publish in releases. Next add actions, then AI-drafted weekly briefs. Actions arrive as stacked steps. Bill summaries and action candidates are implemented. Next come AI drafting of action records, then action review, then action publication. Most observed source change is new press releases, not in-place edits, so statements carry most of the volume. Build the document-change gate (phase 4) as our own check history produces in-place edits to label. Reach MVP functionality, correctness, and a sufficient citizen/reviewer UI locally before production Auth0 verification or production infrastructure. Keep the approved editorial UI direction and [review rules](architecture.md#approved-document-change-review-rules).

After MVP functionality and UI are satisfactory, prepare the application for the agreed Hetzner production deployment using containers, persistent PostgreSQL/capture storage, Terraform, and CI. Serve versioned static public releases on the same CPX21 through a separate static file server. Before exposing the shared workspace, verify access controls, controlled migrations, worker restart/recovery, Hetzner off-server backups and restoration, and operation with the developer workstation off. Select the Hetzner backup destination based on cost and recovery requirements. Infrastructure is not implemented yet. Single-host downtime is accepted initially; public serving must remain independent of the operational application, worker, and database.

Baseline versions before first production deploy. Before the first production deploy, reset the dev database. Collapse the protocol, configuration, schema, parser, and behavior versions to a single baseline version 1. Squash the EF migrations into one initial migration. Delete all older readers. Merge each extraction's parser and normalization versions into one version, and name each rule set by its behavior, for example `html-text-v1` instead of `capture-text-v2`. After the first deploy, versions only grow.

After action publication, unify naming across the analysis features. Use one shared citation span type for document changes, statements, and actions. Move the generic queue handlers, such as `IStatementQueueStore` and `StatementQueueCommands`, into Application/Analysis.

## Reviewer and public experience

Make the source-to-review path understandable to a nontechnical reviewer, with automatic evidence preparation within explicit collection limits, dynamic search across retained evidence and review records, and integrated visual comparison. Global search and expanded reviewer source visibility remain pending decisions. Add an evidence library page to the workspace once document search exists.

Every published record keeps its own human approval of the exact revision. To handle volume, the review queue supports bulk actions, shows the automatic citation and classification checks beside each draft, and sorts the riskiest drafts first. A reviewer can approve a weekly brief together with the records it links, and each record still gets an attributed decision bound to its exact revision. Model output never approves or publishes. Action records can produce about 110 revisions a day, about half of them sponsorship changes on approved records. Review must therefore be as streamlined as possible: bulk approval, and a faster review for a revision where only the code-built sponsorship part changed. That review shows only the change and its citations. The queue holds one item for each record: a newer worker revision replaces an unreviewed one, and review shows the change since the last approved revision. Sponsorship changes draft at once. Measure how often an approved record gets a new revision within a few days; add a waiting window only if that rate is high.

For MVP, add a dynamic SVG visualization of collection and AI processing driven by actual retained process state, with clear waiting, failure, recovery, and budget-limit states and reduced-motion support. Keep AI stages explicitly unavailable until implemented. Public reader pages must preserve the same editorial identity and shared presentation components while exposing published accounts and evidence only.

Public search and filters are part of the reader experience for MVP. Results must show each record's kind, headline, officials, issues, and dates, and link to the record page. Filters must be easy to combine and easy to clear, and the page must work at phone width. Pending decisions: where the index is built and served, how large it can grow, and how it stays bounded as coverage grows. Decide these before action publication, together with release growth: a release holds at most 2,000 records and is rebuilt in full, which action volume will exceed within weeks.

## Shared machinery upgrades

The owner agreed seven upgrades to shared persistence and concurrency code. Build them as stacked PRs in this order. Present the design choices for each item before building it.

1. One advisory lock owner. Six stores write their own `pg_advisory_xact_lock` SQL with four key schemes, and no lock order is written down. Generalize `ReviewWriteLock` into one owner with named keys and a documented order. Keep the trigger key `9911`, or change the triggers in the same migration.
2. A partial index on active `collection_jobs` rows for the worker claim. Finished jobs are never deleted, so the claim scan grows with job history.
3. `FOR UPDATE SKIP LOCKED` in the analysis claim, and an eligibility index that starts with `task`.
4. Comparison and extraction saves verify and diff before they take a lock, and they lock one record instead of the whole store.
5. Stored generated columns for the job definition's source ID and URL, an index for the latest job of each source, and one batched active-root check for each scheduling pass.
6. Parallel evidence processing with a configured degree. This needs item 4 first.
7. Ordered row locks instead of the job store's single global lock. Measure lock waits on the bills-only collection run before the design.

Build items 1 to 3 right after action drafting. Build item 5, then items 4 and 6, together with action review. Build item 7 after the MVP.

## 1. Collection and evidence

Automatic stored-validator selection remains future work. List bills for each Congress from `/v3/bill/{congress}`, for each Congress at or above the configured minimum. The date-window bill list pages through thousands of older bills. The durable engine schedules hourly source checks with per-source cadence and coalesced catch-up, then advances evidence to review. `config/federal-coverage.json` lists all 539 current members of Congress, including delegates, each with a bioguide ID, and 1,392 sources: persistent pages are checked every 6 hours, and feeds and listings every hour. McConnell has no web sources, because his site's robots.txt disallows all paths. Britt and Kim have no press source, because their press listings refuse our requests. Fix the remaining format and parser limits that this coverage found. Version 8 discovery fetches same-host `http` links over `https`, supports feed `xml:base`, and counts excluded links. The House Drupal `rss.xml` feeds stay unused: a sample showed site-wide content, at most 10 items, and no overlap with the press-release listings. Engine-wide spending ceilings, global backlog limits, weighted source fairness, live configuration reload, and a unified operational health view still need explicit policies and implementation. Broad coverage makes these MVP requirements. Automatic discovery of official sources for a configured official is also planned, so that adding an official does not need each source entered by hand. Learn churn for each page region with a two-state Markov chain (stable or churning) over check history, so that recurring churn such as timestamps or rotating widgets is recognized without a hand-written profile for each source. Estimate each page's change rate from check history (a Poisson model, after Cho and Garcia-Molina) and set check cadence from it within the collection budget; read the original work before choosing the refresh policy. Storage must stay bounded at scale. Pending decisions: store each distinct extraction text once, keyed by its hash, and keep raw capture bytes forever only when a gated change, draft, or release uses them or they are the latest version, deleting other bytes after a set period while keeping the observation record. Persistent robots caching and cross-origin robots redirects are outside the current collection policy. Add only packages needed for these capabilities.

Acceptance: add an official through configuration; collect one feed and watched page; demonstrate cancellation/restart without duplicate imports, conditional 304 handling, 429 backoff, bounded discovery, and explicit incomplete-download errors. Validate registry behavior with hundreds of fixture identities without issuing hundreds of live requests.

## 2. Document changes and review foundation

Immutable text extraction, persisted parser/normalization versions, reusable versioned source profiles, and exact local citation spans are implemented. Profiles select one HTML content region and exclude configured subregions without rewriting earlier extractions or citations. Derived document history and immutable bounded contextual comparisons are implemented, including explicit CLI creation and inspection. Version-bound human review is implemented. Implement read-only local MCP inspection. Future cleanup: saved comparisons retain hunk and context text that their exact offsets can rebuild from the two extractions; remove this redundant copy. Comparisons must use compatible parser, normalization, and profile revisions; reprocess both captures when settings change. Profile selection can remove known navigation regions but does not itself identify substantive changes.

The authenticated review workspace provides proposed public records and version-bound review. The first public document-change record must explain the change and its limits in plain language, with both versions and citations accessible from the account. Keep document history available for investigation. Preserve the implemented decision and persistence semantics in shared Application handlers; the throwaway prototype remains the UI reference, not a substitute for these contracts.

The first drafting workflow starts from an operator-selected saved comparison and retains an immutable revision on each explicit save. The Auth0 integration and subject authorization are implemented; live tenant login and production proxy verification remain deferred to deployment. Only the owner can publish or manage access initially. Either authorized operator may approve their own draft; decisions remain individually attributed. Host the shared workspace on Hetzner and verify the existing authentication and authorization before opening shared access.

Acceptance: inspect and publish a real substantive edit with both versions and valid citations. Navigation churn and parser upgrades cannot masquerade as source changes. A reader can distinguish observation time from an established event/edit time and see relevant evidence gaps. A reviewer can assess the proposed public wording beside its sources; changes to approved content or evidence require review of the new version. Approval alone does not publish. Public output remains usable during an operational application outage.

## 3. Statements and actions

Implement offices, dated terms, institutional source ownership (agencies and committees get an institution owner in the schema, not a synthetic person), authoritative action connectors, structured statement extraction, temporal identity resolution, candidate matching, reviewed relationships, and timelines.

Agreed decisions:

- Build statements first: an admitted article's first extraction becomes an AI statement draft that goes through review and publication. Actions, with a Congress.gov connector and offices and terms, come next.
- One statement record covers one source document, such as a press release. It holds the speaker, the date, a short account, and one or more cited quotations. Each quotation stays addressable for later matching.
- An issue gate runs before drafting. A cheap classification tags each article. Only an article that touches a tracked issue gets a full draft and enters review. Other articles stay retained and visible with the reason, and a reviewer can move one into review.
- Issues use two layers. The 32 Congress.gov policy areas are the backbone; they link statements to bills, which carry one policy area each. A small configured list of tracked issues drives the gate and the issue pages. Each tracked issue has an id, a name, a written definition, its parent policy areas, and a dated active range. The definitions are the classification rubric. Verify the policy area names against the Congress.gov API before use.
- Statements mirror the document-change feature: their own queue, runs, drafts, revisions, and review pages, in the same shape. They share only parts that already have one owner: the Core review actor and decision policy, the Anthropic adapter, the citation span checks, and the budget mechanics.
- A statement candidate is the first extraction of an article admitted from a feed or HTML listing. Watched pages stay document-change sources only.
- The first tracked-issue list in `config/issues.json` is a proposal for the owner to edit before use.
- A statement's identity is its admitted article: the source and the URL. A parser upgrade does not create a second statement, and a later edit to the article is a document change.
- This slice includes publication: Publication.Contracts and the static pages gain statement records, so an approved statement publishes end to end.
- Each analysis feature keeps its own queue table. The queue rules have one owner each in Application/Analysis: state transitions, budget reservation and charging, retry backoff, and the worker loop. A feature supplies only its task step and its table mapping.
- The first action slice is bill sponsorship and cosponsorship. One action record covers one bill and lists every tracked official's sponsorship. The bill's bioguide IDs match configured people; the record shows the configured name. Member records are not captured in this slice, because Congress.gov member terms give years only.
- A withdrawn cosponsorship stays in the record with both dates cited.
- The worker writes a new AI revision only when tracked officials' sponsorship, including withdrawals, differs from the current revision. An update to actions alone adds no revision. The record states the date of its evidence.
- Actions get their own review lock key, last in the publication lock order.
- CRS bill summaries are optional cited evidence for the action record. A record must also work without one, because CRS often publishes weeks after introduction. The record cites the newest summary by action date. A new summary version triggers a new AI revision. Drafting does not wait for a summary.
- Pull the shared line-format rules out of the bill renderer when a second renderer needs them.
- Offices and dated terms are a separate step before House votes. Senate votes need senate.gov data and a `lis_member_id` crosswalk, because Congress.gov has neither.
- After the first action slice, build in this order: House votes, then reviewed relationships between statements and actions, then Congressional Record articles as a statement connector. Cosponsorship is cheap position-taking, so votes carry most of the signal about whether words match deeds. Floor speeches cover every member, including officials with no usable web source.
- Use Congress.gov bill subjects when the search filters are designed. Use the member list's `fromDateTime` change check when automatic official discovery is built.

Derived insights are product goals. They make Civic Lens more than a connector to source APIs. Code computes each metric and publishes a method note. A person reviews any judgment before it is published, and each insight shows its coverage limits.

1. Changed record: detect deleted press releases, rewritten quotations, and positions removed from issue pages. Show both versions and their timing next to votes and events. This needs deletion detection and the document-change gate.
2. Shared language: find the same or nearly the same sentences in releases from different offices. Show coordinated messaging, how it spreads, and who uses it first.
3. Talk and action profile: for each official, show the share of statements on each issue next to the share of sponsorships and votes on it.

Acceptance: every statement/action has exact inspectable provenance. Quotation validity and interpretation are separate checks. Human review is bound to the reviewed version. Adding/removing tracked officials requires no code changes and retains history.

## 4. AI drafting, classification, and evaluation

AI drafting of document-change accounts is implemented with Claude Haiku 5.5, a durable analysis queue, immutable runs, and a daily token limit ([architecture](architecture.md#ai-drafting-of-document-change-accounts-implemented)). Classification is a separate stage with its own task versions, budgets, and escalation policy, in the same queue. Weekly briefs and statement/action extraction reuse the same adapter and citation rules.

Classification gates document changes in the MVP. A change classified as nonsubstantive skips review, but it stays visible and inspectable with its reason. Audit a random sample of skipped changes, and let a reviewer move a skipped change back into review. When the engine is unsure, the change goes to review; the engine never drops a change silently. The planned classification ladder is deterministic rules (including learned churn regions), then a cheap probability tier, then a stronger model, then a person. A bake-off selects the models and thresholds for each tier.

The source of truth for classification is human labels made under a written, versioned rubric. The rubric asks narrow typed questions, for example: navigation or boilerplate, date or timestamp only, a changed number, obligation, named person, or policy claim, removed content, and touched configured issues. A label binds to the exact comparison, the rubric version, and the labeler, and it is separate from editorial approval. Model output, including agreement between models, is a prediction and never a label. Labels from the review queue are biased by the gate, so false-negative estimates use the random sample of skipped changes, weighted by its selection rate. Measure agreement between the two reviewers on an overlapping sample.

First steps:

1. Write the labeling rubric and label 150 to 200 changes from our own check history across the federal coverage.
2. Implement deterministic change features: share of text changed, longest change, configured issue terms added or removed, link changes including a removed self-link, and HTTP status or redirect changes.
3. Run a budgeted bake-off on the labeled set: deterministic features alone, logistic regression over those features, Cloudflare Clef-flash and Clef, and Haiku 5.5. Self-hosted encoders are out of scope for now because of the CPX21 memory limit.
4. Set the ladder tiers and thresholds from the measured results.

Verification and confidence (agreed): every model run records a stated confidence from 0 to 1 for each decision it makes. The value is raw and uncalibrated. It drives no routing and is not shown as a score until a calibration map for that task, fitted on human outcomes and versioned, exists. Code-checked results are verified or failed, not scored. The labeled eval set follows with the rubric. A prompt or model change runs against it on demand, within a budget, and ships only without a regression.

Pending decisions: the uncertainty signal for each tier; where the escalation policy and its thresholds live; and the stronger model for the reasoning tier.

Later, use retained evidence and attributed review history to build task-specific validation/golden sets and training or fine-tuning datasets for our own models. Clef has open weights, so fine-tuning on our labels is possible. Editorial approval establishes suitability of a particular account for publication; it does not automatically establish a golden label or eligibility for model training. Before dataset export, agree task labels, example eligibility, source-use permissions, versioned provenance, correction handling, and separate training/validation/test splits that prevent related document versions from leaking across splits. Add task-specific budgets/cache keys, decision explanations, skipped-document sampling, and local evaluation MCP. Ordinary CI must not call paid APIs.

Acceptance: an AI draft of a real substantive edit passes citation checks, and a human approves and publishes it. The gate's false negatives are measured on held-out labeled examples and visible in the skipped-change sample. Budget exhaustion leaves a visible resumable backlog; changing one task version reruns only affected work.

## 5. Weekly briefs

Implement evidence selection, citation-backed drafting, corrections, review mutations, and complete release assembly.

The MVP brief uses only reviewed records. News coverage comes after the first brief. First measure how much of the real record a set of news feeds covers. Add coverage links and a gap signal only if that recall is high. Store feed entries only, never full articles.

Acceptance: publish an issue brief with verified citation links, explicit sample/cutoff, and versioned editorial decisions. AI proposals never become human approvals automatically.

## 6. Public MCP and operational readiness

Implement the separate public MCP deployment over publication artifacts/search index; add source health, backup/restore, worker service management, and deployment automation. Verify live Auth0 login and configure the Hetzner/Terraform infrastructure at this stage, after local MVP functionality, correctness, and UI quality are satisfactory.

Acceptance: public tools never access operational data, mutate records, crawl, or invoke models. Website/MCP use the same release. Demonstrate restore, rollback, release-pinned reads, pagination, bounded traffic behavior, and visible coverage gaps.
