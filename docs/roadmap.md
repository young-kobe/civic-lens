# Roadmap

This page holds pending work only. Current behavior and agreed designs are in the feature docs; start at the [architecture lookup table](architecture.md#lookup-table).

## MVP definition

The MVP delivers the full product: officials' statements, authoritative actions, substantive document changes, and evidence-backed weekly briefs. A release with document changes only is a milestone, not the MVP. Coverage may start small, but all four record kinds must work end to end. Each one collects evidence, keeps exact inspectable provenance, gets human review, and publishes in a static release. AI drafts every record kind, and people edit, approve, and publish. Only human approval stays manual, so review must handle a large volume. Reach MVP features, correctness, and a sufficient citizen and reviewer UI locally before production work starts.

Exit criteria:

- [ ] Statements, actions, document changes, and weekly briefs each run from collection to a published static release.
- [ ] The public release stays usable during an outage of the operational application.
- [ ] Human approval binds to the exact evidence version. Model output never counts as approval.
- [ ] Statements and actions support reviewed relationships and timelines.
- [ ] Document changes separate substantive edits from navigation churn and parser upgrades, through the classification gate.
- [ ] A weekly brief publishes with verified citation links, an explicit sample and cutoff, and versioned editorial decisions.
- [ ] Readers search and filter all published records from the public pages.
- [ ] The review queue handles the drafted volume with bulk tools.
- [ ] The acceptance checklists in the linked feature docs pass.

## Work queue

Each item gives its outcome, what it depends on, and where the design lives. Present the design choices before building R-08 and R-22 only.

### Now

- R-08: Parallel evidence processing with a configured degree. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)
- R-10: Action publication (action step 5). Depends on: none stated. [Actions](architecture/actions.md#limits-and-planned-work)

### Next

- R-11: One shared citation span type, unified analysis naming, and generic queue handlers in Application/Analysis. Depends on: R-10. [Actions](architecture/actions.md#limits-and-planned-work)
- R-12: AI-drafted weekly briefs from reviewed records: evidence selection, cited drafting, corrections, review, and release assembly. Depends on: R-10, because briefs follow actions. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-13: Document-change classification gate: rubric, labels, features, bake-off, and ladder. Depends on: none stated. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-14: Offices and dated terms. Depends on: R-10, the first action slice. [Configuration](architecture/configuration.md#limits-and-planned-work)
- R-15: House roll-call votes as actions. Depends on: R-14. [Actions](architecture/actions.md#limits-and-planned-work)
- R-16: Reviewed relationships between statements and actions, and timelines. Depends on: R-15. [Statements](architecture/statements.md#limits-and-planned-work)
- R-17: Congressional Record articles as a statement connector. Depends on: R-16. [Actions](architecture/actions.md#limits-and-planned-work)
- R-18: Engine policies for broad coverage: spending ceilings, backlog limits, source fairness, live reload, and a health view. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)
- R-19: Review volume tools beyond the sponsorship-only bulk approval of actions, and a clearer source-to-review path. Depends on: none stated. [Review](architecture/review.md#limits-and-planned-work)
- R-20: Public search and filter pages. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
- R-21: Process view of collection and AI stages from retained state. Depends on: none stated. [Host](architecture/host.md#limits-and-planned-work)
- R-29: Verify the policy area names against the Congress.gov API; nothing shows this is done. Depends on: none stated. [Configuration](architecture/configuration.md#limits-and-planned-work)
- R-30: Fix the remaining format and parser limits that the federal coverage found. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)

### Later

- R-22: Ordered row locks in the job store, after the MVP. Depends on: lock waits measured on a bills-only collection run. [Collection](architecture/collection.md#limits-and-planned-work)
- R-23: Baseline versions before the first production deploy. Depends on: none stated. It must come before the first production deploy. [Behavior versions](architecture.md#behavior-versions)
- R-24: Production on the Hetzner CPX21 with containers, Terraform, CI, off-server backups, and live Auth0 verification. Depends on: the MVP. [Host](architecture/host.md#limits-and-planned-work)
- R-25: Public MCP over published releases, with source health, backup and restore, and worker service management. Depends on: the MVP. [Publication](architecture/publication.md#limits-and-planned-work)
- R-26: Read-only local MCP inspection. Depends on: none stated. [Review](architecture/review.md#limits-and-planned-work)
- R-27: Automatic discovery of official sources, and check cadence from measured change rates. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)
- R-28: Datasets and fine-tuning from labels and review history. Depends on: none stated. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-31: Remove the redundant hunk and context copy in saved comparisons. Depends on: none stated. [Documents](architecture/documents.md#limits-and-planned-work)

## Pending decisions

- D-03: Global search and expanded reviewer source visibility. [Review](architecture/review.md#limits-and-planned-work)
- D-04: Bounded storage: store each distinct extraction text once, keyed by its hash. Keep raw capture bytes forever only when a gated change, draft, or release uses them or they are the latest version. Delete other bytes after a set period and keep the observation record. [Collection](architecture/collection.md#limits-and-planned-work)
- D-05: Classification ladder: the uncertainty signal for each tier, where the escalation policy and thresholds live, and the stronger model for the reasoning tier. [Analysis](architecture/analysis.md#limits-and-planned-work)
- D-06: The Hetzner backup destination, chosen by cost and recovery needs. [Host](architecture/host.md#limits-and-planned-work)
- D-07: How the static file server runs locally and in production, and the release retention policy. [Publication](architecture/publication.md#limits-and-planned-work)

## Product direction

The audience is regular citizens who want transparency in U.S. national politics. State and local coverage are possible future extensions. Readers start with reviewed accounts and can inspect the evidence behind them. The editorial flow and the restrained monochrome identity are agreed; detailed public layouts are still to build.

Design for scale. Each record carries rich detail, and coverage of officials, sources, and issues is broad. Automate every step that does not need human judgment.

Derived insights are product goals. They make Civic Lens more than a connector to source APIs. Code computes each metric and publishes a method note. A person reviews any judgment before publication, and each insight shows its coverage limits.

1. Changed record: detect deleted press releases, rewritten quotations, and positions removed from issue pages. Show both versions and their timing next to votes and events. This needs deletion detection and the document-change gate.
2. Shared language: find the same or nearly the same sentences in releases from different offices. Show coordinated messaging, how it spreads, and who uses it first.
3. Talk and action profile: for each official, show the share of statements on each issue next to the share of sponsorships and votes on it.

## Deferred

- News coverage in briefs: deferred until we measure how much of the real record a set of news feeds covers. Add coverage links and a gap signal only if that recall is high. Store feed entries only, never full articles.
- Senate votes, Congress.gov member records, and a waiting window before sponsorship revisions: see [actions](architecture/actions.md#limits-and-planned-work) and [action decisions](architecture/actions.md#decisions).
- Self-hosted encoder models: see [analysis](architecture/analysis.md#limits-and-planned-work).
- Browser rendering, OCR, and cross-origin robots redirects: see [collection](architecture/collection.md#limits-and-planned-work) and [robots](architecture/collection.md#robots-and-pacing).
- Shared line-format rules for renderers: see [bills](architecture/bills.md#limits-and-planned-work).
