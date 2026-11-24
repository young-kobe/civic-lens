# Release 1470 production baseline audit

On 2026-11-24, we ran strictly read-only `REPEATABLE READ READ ONLY` diagnostics (`statement_timeout = 4s`, `lock_timeout = 500ms`, `ROLLBACK`) against `civic-lens-postgres-1` on the production Orin Nano using role `civic_lens_readonly`. Active release was **1470** (1,470 total releases). These observations inform setup experiments 1, 2, 4, and 6 and the solo-maintainer Plane 1 build order.

## Active corpus scale

| Kind | Active rows (release 1470) | Notes |
|---|---:|---|
| `action` (bills) | 19,649 | 6,227 (31.7%) have CRS summaries; 3,927 distinct bills have extracted text in `bill_text_versions`; only 13 bills have 2 or more captured editions; 113 have drafted `why_it_matters`. |
| `vote` | 1,597 | 50 distinct `(chamber, kind, question)` tuples across 48 distinct `(chamber, question)` strings. |
| `executiveAction` | 694 | 694 of 694 (100%) have extracted body text in `executive_action_texts`. |
| `statement` | 33 | 0 published with multiple tracked speakers; 2 approved with `other_speakers`. |
| `relationship` | 0 | 6 rows in `relationship_analysis` (Pending) and 6 in `relationship_drafts` (AwaitingReview). |
| `publication_withdrawals` | 0 | Zero historical withdrawals so far. |

Across all 1,470 releases, `release_issues` holds **7,168 total rows**, **16 distinct wordings**, and **1,840 kB** total disk size, confirming no storage or latency bottleneck.

## Statement hold bottleneck (setup experiment 1)

Of **1,059** statement drafts in `statement_drafts`, **965 (91.1%)** are held in `AwaitingReview` with a `held_reason`, and **61** await review with no `held_reason`:

| Hold reason | Drafts | Root cause observed |
|---|---:|---|
| `The office owner of the source is not the only speaker of the draft.` (0 `other_speakers`) | 268 | **255 of 268** were drafted between 2026-11-01 and 2026-11-08 before `officeOf` was added to 1,392 sources in `collection_configuration_revisions` (`2de419eed6df` had 0 sources with `officeOf`; `a7474510d0bb` added 1,392). Only 13 still lack `officeOf`. |
| `The office owner of the source is not the only speaker of the draft.` (with `other_speakers`) | 428 | Joint or bipartisan press releases quoting other members or local officials. The existing guard blocks multi-speaker code approval as designed. |
| `A person must confirm the AI-proposed statement date by saving a revision before approval.` | 269 | **269 of 269 (100%)** already have a verified `statement_date_evidence_extraction_id` span. **232 of 269** are press releases dated more than 14 days before first capture (167 at 15 to 90 days, 56 at 91 to 365 days, 9 over 365 days), which `StatementCodeApproval.IsDateReadByCode` rejects because of `StatementDateWindowDays`. |

**Consequence.** Re-evaluating the 255 pre-`officeOf` single-speaker drafts against current configuration and handling verified historical date spans safely unlocks a large statement corpus for a solo maintainer with zero LLM spend, while keeping all 428 multi-speaker releases gated.

## Plane 1 deterministic readiness

- **Sponsorship dates.** All **201,070 of 201,070 (100%)** active `action_revision_sponsorships` rows store `sponsored_on`, and 81 store `withdrawn_on`. Edition-bound sponsorships only require multi-edition bill capture (currently only 13 bills have 2+ editions).
- **Vote question templates and motion polarity.** All 1,597 active votes map to 50 `(chamber, kind, question)` tuples. Over 40% are procedural motions: Cloture (292), MotionToProceed (88), House MotionToRecommit (70), House Rule (66), House PreviousQuestion (62), Senate `Other / On the Motion` (79), Discharge (33), Table (25), and Senate `Other / On the Motion to Recommit` (7). Adding `MotionToTable`, `MotionToDischarge`, and Senate `MotionToRecommit` closes the main `Other` procedural gaps deterministically.
- **Bill passage methods (R-69).** Across all **1,510** passage and chamber-agreement rows in `bill_actions`, **100% (0 unmatched)** fall into four exact shapes: voice vote (**745**, 49.3%), recorded tally (**424**, 28.1%, including ~80 rows with a `, N Present` suffix that `BillSteps.VoteCount` previously missed), unanimous consent (**334**, 22.1%), and without objection (**7**, 0.5%).

## Correlated `LATERAL` cast query latency (setup experiment 6)

Running [lateral-cast-query.sql](lateral-cast-query.sql) with `EXPLAIN (ANALYZE, BUFFERS)` on release 1470:
- **2-bill 3-hop query (H.R. 2102 and H.R. 9600, 866 member casts):** **13.7 ms** execution time (planning 8.2 ms), compared to the earlier 4-second plain-join timeout.
- **20-vote 3-hop query (5,995 member casts):** **115.1 ms** cold execution time (304 shared buffer reads; Index Only Scan on `ix_vote_member_list_entries_members`).

PostgreSQL with correlated `LATERAL` queries handles 3-hop Evidence Path neighborhoods on the Orin Nano without a separate graph database.

## Frozen release export (D-22)

[export-frozen-release.sql](export-frozen-release.sql) reads the active release snapshot through `public_api` views in one `REPEATABLE READ READ ONLY` transaction. Keep exported snapshots and `SHA256SUMS` outside Git.
