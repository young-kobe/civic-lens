# Remaining implementation

Phase 0's repository foundation is implemented. The following is planned work, not available functionality. Remove completed work and update the current architecture/operations references; Git preserves previous plans.

## 1. Collection and evidence

Implement versioned JSON/JSONL contracts, configuration validation, identities/source relationships, application-owned Postgres jobs, collector HTTP/feed/scoped-discovery behavior, immutable captures, and idempotent imports. Add only packages needed for this work.

Acceptance: add an official through configuration; collect one feed and watched page; demonstrate cancellation/restart without duplicate imports, conditional 304 handling, 429 backoff, bounded discovery, and explicit incomplete-download errors. Validate registry behavior with hundreds of fixture identities without issuing hundreds of live requests.

## 2. Document changes and review foundation

Implement normalized document versions, contextual diffs, local evidence viewer, first static publication, and read-only local MCP inspection.

Acceptance: inspect and publish a real substantive edit with both versions and valid citations. Navigation churn and parser upgrades cannot masquerade as source changes. Public output remains usable with the local application off.

## 3. Statements and actions

Implement authoritative action connectors, structured statement extraction, temporal identity resolution, candidate matching, reviewed relationships, and timelines.

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
