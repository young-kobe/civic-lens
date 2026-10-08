# Remaining implementation

## MVP goal

The MVP delivers the full product proposition: officials' statements, authoritative actions, substantive document changes, and evidence-backed weekly briefs. A document-change-only release is an intermediate milestone, not the MVP.

The initial audience is regular citizens seeking transparency in U.S. national politics. Follow the [approved editorial experience](architecture.md#approved-editorial-experience-target): readable reviewed accounts lead to inspectable evidence, and private review presents the proposed public record beside its sources. State and local coverage are possible future extensions. The editorial flow and restrained monochrome identity are agreed; detailed public layouts remain to be built.

Initial coverage may be small, but all four capabilities must work end to end: collect evidence, retain exact inspectable provenance, review proposed output, and publish a static release that remains usable during an operational application outage. Human approval must bind to the exact evidence version; model output never constitutes approval. Statements and actions must support reviewed relationships and timelines, document changes must distinguish substantive edits from navigation churn and parser upgrades, and weekly briefs must use verified citations and explicit coverage cutoffs.

The phases below sequence delivery toward this goal. Completing collection or document changes alone does not satisfy it. Classification gates remain conditional on evaluation evidence rather than a prerequisite for shipping all four capabilities.

Phase 0, the watched-page tracer, version 7 collection contracts with v3-v6 recovery support, Core collection-import rules, the Application collection/import handler, the Postgres adapter with its initial migration, CLI database setup and collection/import, durable receipt handoff/replay, managed jobs with fenced ownership, aggregate budgets, retries, cancellation, and origin pacing/backoff, and a separately launched sequential queue worker are implemented. RSS/Atom and scoped HTML discovery with explicit bounded article admission are implemented. Version 7 adds tolerant HTML discovery recovery; `capture-text-v2` extracts the retained Warren article captures with the configured profile. The authenticated review workspace includes owner-only source collection, cancellation, admission, extraction, comparison, and article recollection actions. Bounded robots handling and durable crawl-delay pacing are implemented. Configuration v2 dated coverage/names and immutable configuration revisions bound to managed jobs are also implemented. Current live Auth0 login remains unverified. The following is remaining planned work, not available functionality. Remove completed work and update the current architecture/operations references; Git preserves previous plans.

## Next delivery milestone

The first document-change review workflow is implemented: browse saved comparisons, create drafts, inspect preview/evidence, save immutable revisions, and record attributed approval, requested changes, or withdrawal. Shared monochrome design tokens and a static-renderable record component establish the first reusable UI slice. Next extend the public-facing components from the approved record and implement a separate publication action and static release from exact approved revisions, then complete statements/actions and weekly briefs. Reach MVP functionality, correctness, and a sufficient citizen/reviewer UI locally before production Auth0 verification or production infrastructure. AI is not a prerequisite. Keep the approved editorial UI direction and [review rules](architecture.md#approved-document-change-review-rules).

After MVP functionality and UI are satisfactory, prepare the application for the agreed Hetzner production deployment using containers, persistent PostgreSQL/capture storage, Terraform, and CI. Serve versioned static public releases on the same CPX21 through a separate static file server. Before exposing the shared workspace, verify access controls, controlled migrations, worker restart/recovery, Hetzner off-server backups and restoration, and operation with the developer workstation off. Select the Hetzner backup destination based on cost and recovery requirements. Infrastructure is not implemented yet. Single-host downtime is accepted initially; public serving must remain independent of the operational application, worker, and database.

## 1. Collection and evidence

Automatic stored-validator selection and periodic source scheduling remain future work. The explicit queue worker dispatches already admitted jobs; it does not create recurring jobs. Persistent robots caching and cross-origin robots redirects are outside the current collection policy. Add only packages needed for these capabilities.

Acceptance: add an official through configuration; collect one feed and watched page; demonstrate cancellation/restart without duplicate imports, conditional 304 handling, 429 backoff, bounded discovery, and explicit incomplete-download errors. Validate registry behavior with hundreds of fixture identities without issuing hundreds of live requests.

## 2. Document changes and review foundation

Immutable text extraction, persisted parser/normalization versions, reusable versioned source profiles, and exact local citation spans are implemented. Profiles select one HTML content region and exclude configured subregions without rewriting earlier extractions or citations. Derived document history and immutable bounded contextual comparisons are implemented, including explicit CLI creation and inspection. The local read-only evidence viewer is implemented with history, extraction, saved-comparison, and citation lookup. Version-bound human review is implemented. Implement first static publication and read-only local MCP inspection. Comparisons must use compatible parser, normalization, and profile revisions; reprocess both captures when settings change. Profile selection can remove known navigation regions but does not itself identify substantive changes.

The authenticated review workspace provides proposed public records and version-bound review. Add the first static publication. The first public document-change record must explain the change and its limits in plain language, with both versions and citations accessible from the account. Keep document history available for investigation. Preserve the implemented decision and persistence semantics in shared Application handlers; the throwaway prototype remains the UI reference, not a substitute for these contracts.

The first drafting workflow starts from an operator-selected saved comparison and retains an immutable revision on each explicit save. The Auth0 integration and subject authorization are implemented; live tenant login and production proxy verification remain deferred to deployment. Only the owner can publish or manage access initially. Either authorized operator may approve their own draft; decisions remain individually attributed. Host the shared workspace on Hetzner and verify the existing authentication and authorization before opening shared access.

Acceptance: inspect and publish a real substantive edit with both versions and valid citations. Navigation churn and parser upgrades cannot masquerade as source changes. A reader can distinguish observation time from an established event/edit time and see relevant evidence gaps. A reviewer can assess the proposed public wording beside its sources; changes to approved content or evidence require review of the new version. Approval alone does not publish. Public output remains usable during an operational application outage.

## 3. Statements and actions

Implement offices, dated terms, institutional source ownership, authoritative action connectors, structured statement extraction, temporal identity resolution, candidate matching, reviewed relationships, and timelines.

Acceptance: every statement/action has exact inspectable provenance. Quotation validity and interpretation are separate checks. Human review is bound to the reviewed version. Adding/removing tracked officials requires no code changes and retains history.

## 4. Classification and evaluation

Benchmark Clef and Flash against deterministic and generation baselines. An early bounded experiment may happen during phase 1. Establish reviewed fixtures and held-out evaluations before enforcing admission. Add task-specific budgets/cache keys, decision explanations, skipped-document sampling, and local evaluation MCP.

Acceptance: measured false negatives, calibration, latency, and net cost justify enabled gates. Budget exhaustion leaves a visible resumable backlog; changing one task version reruns only affected work.

## 5. Weekly briefs

Implement evidence selection, citation-backed drafting, corrections, review mutations, and complete release assembly.

Acceptance: publish an issue brief with verified citation links, explicit sample/cutoff, and versioned editorial decisions. AI proposals never become human approvals automatically.

## 6. Public MCP and operational readiness

Implement the separate public MCP deployment over publication artifacts/search index; add source health, backup/restore, release switching, rollback, worker service management, and deployment automation. Verify live Auth0 login and configure the Hetzner/Terraform infrastructure at this stage, after local MVP functionality, correctness, and UI quality are satisfactory.

Acceptance: public tools never access operational data, mutate records, crawl, or invoke models. Website/MCP use the same release. Demonstrate restore, rollback, release-pinned reads, pagination, bounded traffic behavior, and visible coverage gaps.
