# Remaining implementation

## MVP goal

The MVP delivers the full product proposition: officials' statements, authoritative actions, substantive document changes, and evidence-backed weekly briefs. A document-change-only release is an intermediate milestone, not the MVP.

Initial coverage may be small, but all four capabilities must work end to end: collect evidence, retain exact inspectable provenance, review proposed output, and publish a static release that remains usable while the local application is off. Human approval must bind to the exact evidence version; model output never constitutes approval. Statements and actions must support reviewed relationships and timelines, document changes must distinguish substantive edits from navigation churn and parser upgrades, and weekly briefs must use verified citations and explicit coverage cutoffs.

The phases below sequence delivery toward this goal. Completing collection or document changes alone does not satisfy it. Classification gates remain conditional on evaluation evidence rather than a prerequisite for shipping all four capabilities.

Phase 0, the watched-page tracer, version 5 collection contracts with v3 page and v4 feed recovery support, Core collection-import rules, the Application collection/import handler, the Postgres adapter with its initial migration, CLI database setup and collection/import, durable receipt handoff/replay, and managed jobs with fenced ownership, aggregate budgets, retries, cancellation, and origin pacing/backoff, plus RSS/Atom and scoped HTML discovery with explicit bounded article admission are implemented. Configuration v2 dated coverage/names and immutable configuration revisions bound to managed jobs are also implemented. The following is remaining planned work, not available functionality. Remove completed work and update the current architecture/operations references; Git preserves previous plans.

## 1. Collection and evidence

Complete robots handling. Automatic stored-validator selection and background scheduling remain future work. Add only packages needed for these capabilities.

Acceptance: add an official through configuration; collect one feed and watched page; demonstrate cancellation/restart without duplicate imports, conditional 304 handling, 429 backoff, bounded discovery, and explicit incomplete-download errors. Validate registry behavior with hundreds of fixture identities without issuing hundreds of live requests.

## 2. Document changes and review foundation

Immutable text extraction, persisted parser/normalization versions, and exact local citation spans are implemented. Build normalized document-version history and contextual diffs on these extraction records; implement the local evidence viewer, version-bound human review, first static publication, and read-only local MCP inspection. Text extraction alone does not identify substantive changes or remove navigation churn.

Acceptance: inspect and publish a real substantive edit with both versions and valid citations. Navigation churn and parser upgrades cannot masquerade as source changes. Public output remains usable with the local application off.

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

Implement the separate public Worker over publication artifacts/search index; add source health, backup/restore, release switching, rollback, and deployment automation.

Acceptance: public tools never access operational data, mutate records, crawl, or invoke models. Website/MCP use the same release. Demonstrate restore, rollback, release-pinned reads, pagination, bounded traffic behavior, and visible coverage gaps.
