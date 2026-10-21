# Roadmap

This page holds pending work only. Current behavior and agreed designs are in the feature docs; start at the [architecture lookup table](architecture.md#lookup-table).

## MVP definition

The MVP delivers the full product: officials' statements, authoritative actions, substantive document changes, and evidence-backed weekly briefs. A release with document changes only is a milestone, not the MVP. Coverage may start small, but all four record kinds must work end to end. Each one collects evidence, keeps exact inspectable provenance, gets human review, and publishes in a static release. AI drafts every record kind. Code approves action and House vote records after it checks their citations (D-15, accepted on 2026-10-19). People edit and approve the other kinds, and the owner publishes. Review must handle a large volume of statements, relationships, briefs, and document changes. The dev box reaches the features, correctness, and citizen and reviewer UI of the first deploy before the first cloud deployment. Public MCP, the shared language insight, and the document-change criterion are checked after that deployment.

Exit criteria for the first deploy:

- [ ] Statements, actions, document changes, and weekly briefs each run from collection to a published static release.
- [ ] The public release stays usable during an outage of the operational application.
- [ ] Approval binds to the exact evidence version. Model output never counts as approval, and code approves only official fact records. People review the other kinds.
- [ ] Statements and actions support reviewed relationships and timelines.
- [ ] A weekly brief publishes with verified citation links, an explicit sample and cutoff, and versioned editorial decisions.
- [ ] Readers search and filter all published records from the public pages.
- [ ] Readers can report a problem with a published record, and each report reaches a person in the review queue.
- [ ] A person can publish a correction or a withdrawal of any record kind.
- [ ] House and Senate votes both publish.
- [ ] The talk and action profile publishes with a method note.
- [ ] The acceptance checklists in the linked feature docs pass, except the public MCP acceptance checklist.

After the first deploy:

- [ ] Public MCP reads the same releases as the website.
- [ ] The shared language insight publishes with a method note.
- [ ] Document changes separate substantive edits from navigation churn and parser upgrades. Separate extraction streams isolate parser upgrades. Rules from measured change history filter churn, and a model tier is added only when the measurement shows a need.

## Work queue

Each item gives its outcome, what it depends on, and where the design lives. Present the design choices before building R-22 only.

The first deploy is a complete product within the product direction. Its path is R-36, R-32, R-42, and R-43, then R-23, then R-24. R-25, R-38, R-21, and R-18 come after R-24.

### Next

- R-17: Congressional Record articles as a statement connector. Depends on: none stated. [Actions](architecture/actions.md#limits-and-planned-work)
- R-36: Official pages and timelines for citizens. Each record shows its evidence and timeline. The official pages are also public filter pages, and they show offices and dated terms from the [roster](architecture/roster.md). Each record page links to the report page of the search service. The weekly briefing home page and the issue and policy area pages are built. Each release also holds `officials.json` with the offices and dated terms, and `catalog.json`. The official pages, the officials list, the vote index files, and the config `offices` array are built. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
- R-32: Correction and withdrawal releases for all record kinds. No kind can publish one yet. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
- R-42: Senate votes from senate.gov data, as House votes come from the House Clerk. Members match through the Senate member IDs of the [roster](architecture/roster.md). Depends on: none stated. [Votes](architecture/votes.md#limits-and-planned-work)
- R-43: The talk and action profile insight. For each official, code shows the share of statements on each issue next to the share of sponsorships and votes on it, and publishes a method note. It uses no model. Depends on: R-36. [Publication](architecture/publication.md#limits-and-planned-work)
- R-39: An `analyses redraft --stale` owner command requeues finished drafts whose last run used an older task, prompt, or schema version. It covers document changes, statements, actions, votes, and briefs. Depends on: none stated. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-29: Check each policy area name against the Congress.gov API. Depends on: none stated. [Configuration](architecture/configuration.md#limits-and-planned-work)
- R-30: Fix the remaining format and parser limits that the federal coverage found. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)

### Later

- R-25: Public MCP over published releases, and source health. Depends on: R-24. [Publication](architecture/publication.md#limits-and-planned-work)
- R-38: The shared language insight. Code finds the same or nearly the same sentences in releases from different offices, and publishes a method note. A person reviews each finding before publication. Depends on: R-24. [Statements](architecture/statements.md#limits-and-planned-work)
- R-21: Process view of collection and AI stages from retained state. Depends on: R-24. [Host](architecture/host.md#limits-and-planned-work)
- R-18: An engine-health view, built with R-21. The worker heartbeat, the spending total, and source fairness are in R-24. Depends on: R-24. [Collection](architecture/collection.md#limits-and-planned-work)
- R-13: Measure document change, then gate it. Check the House press index pages and about 300 press release URLs daily for 2 to 4 weeks. Count byte, text, and line changes for each source, and label about 100 changes by hand. Add rules for the churn found. Add a model tier only if the changes that pass the rules are too many for people to review. Depends on: the first cloud deployment (R-24). [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-22: Ordered row locks in the job store, after the MVP. Depends on: lock waits measured on a bills-only collection run. [Collection](architecture/collection.md#limits-and-planned-work)
- R-23: Baseline versions before the first production deploy. Depends on: none stated. It must come before the first production deploy. [Behavior versions](architecture.md#behavior-versions)
- R-24: Production on the Hetzner CPX21 with containers, Terraform, CI, off-server backups, and live Auth0 verification. Public releases upload to Cloudflare R2 behind its CDN. The open host checklist items are its exit checks. Also stop the worker during a job and show that the deployed container recovers it. The search service runs as its own container on the same box. It also adds keep-last-N retention for release directories, because the `factPublishing` stage makes releases without the owner. R-24 also holds backup and restore, worker service management, a worker heartbeat, and the documented spending total. Source fairness is an open choice: a job class column, or a production backfill before press sources start. Decide it before R-24. The coverage file is 771 KB of the 1 MB limit of the shared configuration loader. Raise the limit before R-24 only if the file would pass 1 MB before the deploy. Depends on: the exit criteria of the first deploy and R-23. [Host](architecture/host.md#limits-and-planned-work)
- R-26: Read-only local MCP inspection. Depends on: none stated. [Review](architecture/review.md#limits-and-planned-work)
- R-41: Collection concurrency for backfills, when a measured backlog is more than one worker can clear. The precedent is CIVIC_LENS_EVIDENCE_CONCURRENCY. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)
- R-27: Automatic discovery of official sources, and check cadence from measured change rates. It needs runtime writes, so people, sources, and coverage then move to tables. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)
- R-28: Datasets and fine-tuning from labels and review history. Depends on: none stated. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-31: Remove the redundant hunk and context copy in saved comparisons. Depends on: none stated. [Documents](architecture/documents.md#limits-and-planned-work)

## Pending decisions

- D-03: Global search and expanded reviewer source visibility. [Review](architecture/review.md#limits-and-planned-work)
- D-04: Bounded storage: store each distinct extraction text once, keyed by its hash. Keep raw capture bytes forever only when a gated change, draft, or release uses them or they are the latest version. Delete other bytes after a set period and keep the observation record. [Collection](architecture/collection.md#limits-and-planned-work)
- D-05: Classification ladder, only if the R-13 measurement shows that a model tier is needed: the uncertainty signal for each tier, where the escalation policy and thresholds live, and the stronger model for the reasoning tier. [Analysis](architecture/analysis.md#limits-and-planned-work)
- D-11: Drafting the past votes of a member who is added to tracking later. [Votes](architecture/votes.md#limits-and-planned-work)
- D-12: Content profiles for the 282 of 1,392 page, HTML, and feed sources that have none. [Documents](architecture/documents.md#limits-and-planned-work)
- D-13: A classifier benchmark for the statement issue gate, to run as soon as possible. The gate label store, the held-out split, the owner labeling page, and the label export are built ([Statements](architecture/statements.md#gate-labels)). Still to build: a Sonnet pre-label for releases outside the held-out set, and about 100 labels by hand, starting from the 48 measured ones. Then compare Haiku 5.5, Clef-flash, Clef-omni, Clef, a fine-tuned Clef, Strands Decider 2B, a fine-tuned ModernBERT, and a feature baseline. The issue list is configuration. A decision model takes the options in each request, so it needs no retraining when the list changes. A model that runs inside the worker (ONNX Runtime) needs a reviewed boundary change first. The fine-tuning needs the dataset rules of [R-28](#later) agreed first.

## Product direction

The audience is regular citizens who want transparency in U.S. national politics. State and local coverage are possible future extensions. Readers start with reviewed accounts and can inspect the evidence behind them. The editorial flow and the restrained monochrome identity are agreed; detailed public layouts are still to build.

Design for scale. Each record carries rich detail, and coverage of officials, sources, and issues is broad. Automate every step that does not need human judgment.

Derived insights are product goals. They make Civic Lens more than a connector to source APIs. Code computes each metric and publishes a method note. A person reviews any judgment before publication, and each insight shows its coverage limits.

1. Changed record: detect deleted press releases, rewritten quotations, and positions removed from issue pages. Show both versions and their timing next to votes and events. This needs deletion detection and the document-change gate.
2. Shared language: find the same or nearly the same sentences in releases from different offices. Show coordinated messaging, how it spreads, and who uses it first.
3. Talk and action profile: for each official, show the share of statements on each issue next to the share of sponsorships and votes on it.

## Deferred

- News coverage in briefs: deferred until we measure how much of the real record a set of news feeds covers. Add coverage links and a gap signal only if that recall is high. Store feed entries only, never full articles.
- Congress.gov member records and a waiting window before sponsorship revisions: see [actions](architecture/actions.md#limits-and-planned-work) and [action decisions](architecture/actions.md#decisions).
- Self-hosted encoder models: see [analysis](architecture/analysis.md#limits-and-planned-work).
- Browser rendering, OCR, and cross-origin robots redirects: see [collection](architecture/collection.md#limits-and-planned-work) and [robots](architecture/collection.md#robots-and-pacing).
- Shared line-format rules for renderers: see [bills](architecture/bills.md#limits-and-planned-work).
