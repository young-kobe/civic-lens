# Remaining implementation

## MVP goal

The MVP delivers the full product proposition: officials' statements, authoritative actions, substantive document changes, and evidence-backed weekly briefs. A document-change-only release is an intermediate milestone, not the MVP.

The initial audience is regular citizens seeking transparency in U.S. national politics. Follow the [approved editorial experience](architecture.md#approved-editorial-experience-target): readable reviewed accounts lead to inspectable evidence, and private review presents the proposed public record beside its sources. State and local coverage are possible future extensions. The editorial flow and restrained monochrome identity are agreed; detailed public layouts remain to be built.

Initial coverage may be small, but all four capabilities must work end to end: collect evidence, retain exact inspectable provenance, review proposed output, and publish a static release that remains usable during an operational application outage. Human approval must bind to the exact evidence version; model output never constitutes approval. Statements and actions must support reviewed relationships and timelines, document changes must distinguish substantive edits from navigation churn and parser upgrades, and weekly briefs must use verified citations and explicit coverage cutoffs.

AI drafting and classification are MVP requirements. AI drafts document-change accounts, statement and action records, and weekly briefs. Reviewers edit, approve, and publish these drafts. A classification gate keeps navigation churn and other nonsubstantive changes out of review.

Design for scale. The product needs rich detail in each record and broad coverage of officials, sources, and issues. Automate every step that does not need human judgment. The worker drafts each change that passes the gate; no operator starts it. Human approval remains the only manual step before publication, so review must handle a large volume (see [Reviewer and public experience](#reviewer-and-public-experience)).

The phases below sequence delivery toward this goal. Completing collection or document changes alone does not satisfy it.

Phase 0, the watched-page tracer, version 7 collection contracts with v3-v6 recovery support, Core collection-import rules, the Application collection/import handler, the Postgres adapter with its initial migration, CLI database setup and collection/import, durable receipt handoff/replay, managed jobs with fenced ownership, aggregate budgets, retries, cancellation, and origin pacing/backoff, and a separately launched sequential queue worker are implemented. RSS/Atom and scoped HTML discovery with explicit bounded article admission are implemented. Version 7 adds tolerant HTML discovery recovery; `capture-text-v2` extracts the retained Warren article captures with the configured profile. The authenticated review workspace includes owner-only source collection, cancellation, admission, extraction, comparison, and article recollection actions. Bounded robots handling and durable crawl-delay pacing are implemented. Configuration v2 dated coverage/names and immutable configuration revisions bound to managed jobs are also implemented. Current live Auth0 login remains unverified. The following is remaining planned work, not available functionality. Remove completed work and update the current architecture/operations references; Git preserves previous plans.

## Next delivery milestone

The first document-change review workflow is implemented: browse saved comparisons, create drafts, inspect preview/evidence, save immutable revisions, and record attributed approval, requested changes, or withdrawal. The owner can publish approved revisions as an immutable static release from the workspace or the CLI, with atomic activation and CLI rollback. The worker now writes an AI draft of each document-change account ([architecture](architecture.md#ai-drafting-of-document-change-accounts-implemented)). Next add statements and actions with AI extraction and relevance and issue classification, then AI-drafted weekly briefs. Most observed source change is new press releases, not in-place edits, so statements carry most of the volume. Build the document-change gate (phase 4) as our own check history produces in-place edits to label. Reach MVP functionality, correctness, and a sufficient citizen/reviewer UI locally before production Auth0 verification or production infrastructure. Keep the approved editorial UI direction and [review rules](architecture.md#approved-document-change-review-rules).

After MVP functionality and UI are satisfactory, prepare the application for the agreed Hetzner production deployment using containers, persistent PostgreSQL/capture storage, Terraform, and CI. Serve versioned static public releases on the same CPX21 through a separate static file server. Before exposing the shared workspace, verify access controls, controlled migrations, worker restart/recovery, Hetzner off-server backups and restoration, and operation with the developer workstation off. Select the Hetzner backup destination based on cost and recovery requirements. Infrastructure is not implemented yet. Single-host downtime is accepted initially; public serving must remain independent of the operational application, worker, and database.

## Reviewer and public experience

Make the source-to-review path understandable to a nontechnical reviewer, with automatic evidence preparation within explicit collection limits, dynamic search across retained evidence and review records, and integrated visual comparison. Global search and expanded reviewer source visibility remain pending decisions. Add an evidence library page to the workspace once document search exists.

Every published record keeps its own human approval of the exact revision. To handle volume, the review queue supports bulk actions, shows the automatic citation and classification checks beside each draft, and sorts the riskiest drafts first. A reviewer can approve a weekly brief together with the records it links, and each record still gets an attributed decision bound to its exact revision. Model output never approves or publishes.

For MVP, add a dynamic SVG visualization of collection and AI processing driven by actual retained process state, with clear waiting, failure, recovery, and budget-limit states and reduced-motion support. Keep AI stages explicitly unavailable until implemented. Public reader pages must preserve the same editorial identity and shared presentation components while exposing published accounts and evidence only.

## 1. Collection and evidence

Automatic stored-validator selection remains future work. The durable engine schedules hourly source checks with per-source cadence and coalesced catch-up, then advances evidence to review. `config/federal-coverage.json` covers all 533 members of Congress with 1,384 sources: persistent pages are checked every 6 hours, and feeds and listings every hour. After AI drafting, fix the format and parser limits that this coverage found, starting with feeds that silently drop items with `http://` links. Engine-wide spending ceilings, global backlog limits, weighted source fairness, live configuration reload, and a unified operational health view still need explicit policies and implementation. Broad coverage makes these MVP requirements. Automatic discovery of official sources for a configured official is also planned, so that adding an official does not need each source entered by hand. Learn churn for each page region with a two-state Markov chain (stable or churning) over check history, so that recurring churn such as timestamps or rotating widgets is recognized without a hand-written profile for each source. Estimate each page's change rate from check history (a Poisson model, after Cho and Garcia-Molina) and set check cadence from it within the collection budget; read the original work before choosing the refresh policy. Storage must stay bounded at scale. Pending decisions: store each distinct extraction text once, keyed by its hash, and keep raw capture bytes forever only when a gated change, draft, or release uses them or they are the latest version, deleting other bytes after a set period while keeping the observation record. Persistent robots caching and cross-origin robots redirects are outside the current collection policy. Add only packages needed for these capabilities.

Acceptance: add an official through configuration; collect one feed and watched page; demonstrate cancellation/restart without duplicate imports, conditional 304 handling, 429 backoff, bounded discovery, and explicit incomplete-download errors. Validate registry behavior with hundreds of fixture identities without issuing hundreds of live requests.

## 2. Document changes and review foundation

Immutable text extraction, persisted parser/normalization versions, reusable versioned source profiles, and exact local citation spans are implemented. Profiles select one HTML content region and exclude configured subregions without rewriting earlier extractions or citations. Derived document history and immutable bounded contextual comparisons are implemented, including explicit CLI creation and inspection. Version-bound human review is implemented. Implement read-only local MCP inspection. Future cleanup: saved comparisons retain hunk and context text that their exact offsets can rebuild from the two extractions; remove this redundant copy. Comparisons must use compatible parser, normalization, and profile revisions; reprocess both captures when settings change. Profile selection can remove known navigation regions but does not itself identify substantive changes.

The authenticated review workspace provides proposed public records and version-bound review. The first public document-change record must explain the change and its limits in plain language, with both versions and citations accessible from the account. Keep document history available for investigation. Preserve the implemented decision and persistence semantics in shared Application handlers; the throwaway prototype remains the UI reference, not a substitute for these contracts.

The first drafting workflow starts from an operator-selected saved comparison and retains an immutable revision on each explicit save. The Auth0 integration and subject authorization are implemented; live tenant login and production proxy verification remain deferred to deployment. Only the owner can publish or manage access initially. Either authorized operator may approve their own draft; decisions remain individually attributed. Host the shared workspace on Hetzner and verify the existing authentication and authorization before opening shared access.

Acceptance: inspect and publish a real substantive edit with both versions and valid citations. Navigation churn and parser upgrades cannot masquerade as source changes. A reader can distinguish observation time from an established event/edit time and see relevant evidence gaps. A reviewer can assess the proposed public wording beside its sources; changes to approved content or evidence require review of the new version. Approval alone does not publish. Public output remains usable during an operational application outage.

## 3. Statements and actions

Implement offices, dated terms, institutional source ownership (agencies and committees get an institution owner in the schema, not a synthetic person), authoritative action connectors, structured statement extraction, temporal identity resolution, candidate matching, reviewed relationships, and timelines.

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

Pending decisions: the uncertainty signal for each tier; where the escalation policy and its thresholds live; and the stronger model for the reasoning tier.

Later, use retained evidence and attributed review history to build task-specific validation/golden sets and training or fine-tuning datasets for our own models. Clef has open weights, so fine-tuning on our labels is possible. Editorial approval establishes suitability of a particular account for publication; it does not automatically establish a golden label or eligibility for model training. Before dataset export, agree task labels, example eligibility, source-use permissions, versioned provenance, correction handling, and separate training/validation/test splits that prevent related document versions from leaking across splits. Add task-specific budgets/cache keys, decision explanations, skipped-document sampling, and local evaluation MCP. Ordinary CI must not call paid APIs.

Acceptance: an AI draft of a real substantive edit passes citation checks, and a human approves and publishes it. The gate's false negatives are measured on held-out labeled examples and visible in the skipped-change sample. Budget exhaustion leaves a visible resumable backlog; changing one task version reruns only affected work.

## 5. Weekly briefs

Implement evidence selection, citation-backed drafting, corrections, review mutations, and complete release assembly.

Acceptance: publish an issue brief with verified citation links, explicit sample/cutoff, and versioned editorial decisions. AI proposals never become human approvals automatically.

## 6. Public MCP and operational readiness

Implement the separate public MCP deployment over publication artifacts/search index; add source health, backup/restore, worker service management, and deployment automation. Verify live Auth0 login and configure the Hetzner/Terraform infrastructure at this stage, after local MVP functionality, correctness, and UI quality are satisfactory.

Acceptance: public tools never access operational data, mutate records, crawl, or invoke models. Website/MCP use the same release. Demonstrate restore, rollback, release-pinned reads, pagination, bounded traffic behavior, and visible coverage gaps.
