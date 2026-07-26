# API timeline (pre-2026-07, consolidated)

Condensed record of `analysis/src/api/` (FastAPI) history, consolidated from the retired
`docs/walkthroughs/` linear log (see `docs/todos/walkthrough-consolidation.md`). Chronological by
original walkthrough number; most entries carry no in-file date. This layer's whole surface — the
snapshot-cache-backed, pre-`/api/v1`-versioned SQLite-era API — sits inside the stack the ongoing
Postgres rewrite is replacing, so treat every endpoint name below as historical unless it's still
listed in CLAUDE.md.

## 002 — Frontend API Integration (undated)

First real API the React frontend consumed: a "Rich Aggregator API" serving Story Clusters, GOP
Favorability, Public Sentiment, Bot Activity Profiler pages, replacing UI mock data.

## 005 — Python Analysis Refactoring (undated, API slice)

Added a bot-activity endpoint end-to-end as part of the broader dataclass-model refactor.

## 006 — Background Analysis Pipeline (undated)

Changed the API from computing aggregates at request time to serving pre-computed JSON snapshots,
with a new `/api/cache-status` endpoint and fallback path. This request/serve split — heavy
aggregation happens in `job_runner.save_snapshots()`, not the API — is the shape CLAUDE.md still
describes for the FastAPI layer, even though the underlying JSON-file cache is SQLite-era and
retired.

## 013 — Analysis & UI Implementation (undated, API slice)

Initial FastAPI `server.py` stood up alongside the first schema and first React app.

## 020 — X Integration & Global Heatmap (undated, API slice)

Added `/api/geo-sentiment`, the endpoint for the geo-aggregation feature fully decommissioned in
walkthrough 066.

## 024 — Sentiment, Caching, and UI Fixes (undated, API slice)

Added 400 validation on `/api/stories`. The endpoint itself was deleted five walkthroughs later
(029).

## 029 — Clustering Removal (undated, API slice)

Deleted `/api/stories` and `/api/run/clustering` along with the story-clustering feature they served.

## 030 — Audit Remediation, Layers 2-4 (undated, API slice)

No new endpoints, but the migration work in this walkthrough (dropping dead tables, adding
`prompt_versions`) is what the API's `save_ai_output` path started depending on.

## 033 — Narrative Reader Layer (undated, API slice)

Added `GET /api/narratives` with cached snapshots per time window, backing the first Narratives tab.

## 034 — Review UI + ai_output_evals Writers (undated, API slice)

Added three endpoints: `/api/review/queue`, `/api/review/submit`, `/api/review/stats`, deliberately
unauthenticated at this point (reviewer_id from client-side localStorage) with auth explicitly
deferred to a later gate.

## 035 — Goal Narrowing & Honesty Renames (undated, API slice)

Renamed API response fields from `origin_*` to `first_seen_*` to match the schema rename — see the
analysis timeline's 035 entry for the full rationale (the product-goal narrowing this rename serves is
still load-bearing today).

## 036 — Account Tier Classification (undated, API slice)

No dedicated endpoint; `first_seen_tier` was exposed as an additional field on the existing narrative
response shape.

## 041 — Cache Coverage + B1 Versioning (undated, API slice)

Fixed a cache-key mismatch where a hardcoded `narratives_{window}_20` cache key silently missed any
request with a non-default `limit` — the fix caches top-100 once per window and slices at request
time, falling back to live compute above 100.

## 043 — Propaganda Surfaces (undated, API slice)

Added the Propaganda tab's backing endpoint and cache-per-window, plus a Review-queue extension so
propaganda labels could be collected for eventual calibration.

## 045 — Analysis + API Audit Remediation (2026-04-20)

Introduced `/api/v1` versioning (legacy unversioned `/api/*` paths no longer accepted); split
`server.py` from a 380-line monolith into `dependencies.py`, `cache_utils.py`, and
`routers/{health,admin,data,review}.py`; deleted the unused `/api/profiles` endpoint and the legacy
`process_analysis_queue`/`/api/run/analysis` path (`job_runner` became the sole trigger path);
`/health` now actually probes DB + cache and returns `degraded` on failure; pipeline-trigger routes
gained a configurable cooldown (`enforce_trigger_cooldown`, default 60s) against accidental
re-triggering.

## 047 — Pre-Deploy Hardening, PR-A (2026-04-21 launch window)

Added `slowapi`-based rate limiting (Cloudflare-aware key function, per-route overrides) and a
security-headers middleware (CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy,
Permissions-Policy — HSTS deliberately left to Cloudflare).

## 049 — Launch (live 2026-04-21, API slice)

`/health` wasn't reachable in production because Caddy routed everything non-`/api/*` to static SPA
files — fixed with an explicit route matcher. A starlette CVE surfaced by CI's `pip-audit` forced a
`fastapi>=0.120` bump just before cutover.

## 052 — Source Filter + Label Renames (undated, API slice)

`/sentiment` and `/propaganda` routes gained a `?source=news/reddit/social` query param; a non-`all`
value bypasses the snapshot cache and computes live, since the cache key doesn't vary by source.

## 066 — Movers + Geo Decommission (undated, API slice)

Added a live-computed (not cached, rate-limited 20/min) `/movers` endpoint for window-over-window
tone/favorability deltas. Removed `/geo-sentiment` as part of the full geo-sentiment stack
decommission (see the analysis timeline's 066 entry for the rationale).
