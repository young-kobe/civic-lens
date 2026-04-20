# 034 — Review UI + ai_output_evals Writers

## Context

`ai_output_evals` shipped as schema-only in walkthrough 030. This walkthrough wires the writer side: a Review tab in the UI lets a human triage AI outputs, mark them correct/incorrect, optionally provide a corrected label, and flag rows as part of the golden set used for accuracy benchmarks.

Auth is intentionally not landed here — the design supports a `reviewer_id` field for attribution and the endpoints are ready to be gated behind admin auth in a future walkthrough.

## Changes

### Backend

- `analysis/src/reporting/review.py` — new `ReviewService`. Three operations:
  - `get_queue(task, source_type=None, confidence_max=None, limit=20, offset=0)` — joins `ai_outputs` to `docs`, anti-joins `ai_output_evals` to drop already-reviewed rows, orders **lowest confidence first** (review yields the most signal where the model is least sure), returns enriched payloads with doc preview + parsed model output.
  - `submit(...)` — `INSERT OR REPLACE INTO ai_output_evals` with denormalized `doc_id` + `task_type` for fast joins. Re-review of an existing row updates it in place.
  - `get_stats(task=None)` — per-task counts of total outputs, reviewed, correct, incorrect, golden, and observed accuracy %.
- `analysis/src/api/server.py` — three endpoints:
  - `GET /api/review/queue?task=sentiment&source_type=...&confidence_max=...&limit=20&offset=0`
  - `POST /api/review/submit` (Pydantic body: `ReviewSubmission`)
  - `GET /api/review/stats?task=sentiment`

### UI

- `ui/src/types.ts` — `ReviewQueueItem`, `ReviewSubmission`, `ReviewStats`, `ReviewTaskType`.
- `ui/src/services/api.ts` — `fetchReviewQueue`, `submitReview`, `fetchReviewStats`.
- `ui/src/pages/Review.tsx` — new page:
  - Top controls: task selector (sentiment / favorability / bot_detection / claims), confidence-cap filter, reviewer-ID input (persisted to localStorage).
  - Stats bar: total outputs, reviewed (with %), correct/incorrect counts, observed accuracy with traffic-light color coding (≥95% green, ≥80% amber, otherwise red).
  - Single-item review card showing source-text excerpt, model output (label + confidence + evidence spans, or per-claim list for the claims task), and a form for verdict + optional corrected label + slider for human confidence + "add to golden set" checkbox + notes.
  - Submit advances to the next item; queue auto-fetches more when low. Skip is available without writing.
- `ui/src/pages/index.ts`, `ui/src/App.tsx` — wire the Review tab as the rightmost tab.

### Tests

- `analysis/tests/test_review.py` — 7 tests:
  - Queue ordered by confidence ascending.
  - Filters by source_type and by confidence cap.
  - Already-reviewed rows excluded from the queue.
  - `INSERT OR REPLACE` allows re-review.
  - Stats compute correct/incorrect/golden counts and accuracy.
  - Submitting against an unknown ai_output_id raises.

Full Python suite: 42/42 pass. UI typecheck + build clean.

## Notes for adding admin auth later

- All three review endpoints accept a `reviewer_id` in the submission body (or none — defaults to NULL). When auth lands, swap that for the authenticated user from the request context and reject unsigned `POST /api/review/submit` calls.
- The queue/stats endpoints can be admin-gated by adding a single FastAPI dependency; no schema changes needed.
- Consider also gating the entire `/review` UI route in `App.tsx` on a flag from `/api/auth/me`.

## How this unblocks the layer-1 accuracy gap

The audit's largest still-open item was "no golden test set, no calibration." This walkthrough is the first half of closing that:

1. **Reviewer marks rows.** Each click that flags `is_golden=true` adds a hand-validated example to `ai_output_evals` with `human_label` and `is_correct`.
2. **Calibration job (next walkthrough).** A periodic job reads `ai_output_evals WHERE is_golden=1`, computes accuracy per task at multiple confidence thresholds, and emits a calibration curve. With ~100–200 golden rows per task you can credibly claim "at confidence ≥ 0.7 we measure X% accuracy on this golden set."

## Deploy

No new migration. Restart the API to pick up the new endpoints. Reviewer IDs persist in browser localStorage; pass any string (e.g., your name) to attribute reviews.
