# Scale-out and targeted classification — design proposal

> **SUPERSEDED (2026-07-26):** superseded by the Postgres redesign
> (`docs/todos/post-rewrite-cutover.md`); the stay-on-SQLite decision this
> proposal records was reversed.

Status: draft for human decision, approved plan pending implementation. Date: 2026-07-13.

Covers: 90-day data window, local Reddit ingestion + merge, cheap cloud inference tier with Gemini escalation, analyze throughput, web-box isolation (noisy-neighbor mitigation), snapshot read efficiency, and relevance-targeted classification. Also records the decision to stay on SQLite and NOT decouple the crawler/engine to a separate server.

## Context

Prod (Hetzner) went live 2026-07-09 and holds all canonical data; the local WSL DB is a fresh empty schema. Four connected problems:

1. **Data is capped at ~30 days** by two hardcoded filters (Go RSS cutoff, Python ETL `is_recent`). The 90d snapshot window already exists end-to-end (aggregators, API, UI) but never has more than 30 days of docs to aggregate.
2. **Reddit is unreachable from Hetzner** (datacenter IP blocked; the reddit systemd unit was already removed), but public `.json` endpoints work from Kobe's residential IP. Current fetcher is also weak: hardcoded 25 posts per sub, `new.json` only, no pagination, no rate limiting.
3. **Inference**: cheap self-hosted inference in the cloud (Orin idea dropped 2026-07-13) — a second small Hetzner CPU box running Ollama, with confidence-based escalation to Gemini. Remote Ollama via `CIVIC_OLLAMA_HOST` and an `openai_compat` backend (`llm_base_url`/`llm_api_key`, factory.py:80-104) are already first-class config paths.
4. **Throughput**: the analyze pipeline is fully sequential (~4–5 LLM calls per social doc, one at a time), which matters for draining the backfill backlog.

Decisions confirmed with Kobe: Hetzner stays primary (news crawl + analyze + API/UI); local machine only runs the reddit fetcher and ships data over; backfill is best-effort from live sources (no Pushshift/Wayback); X stays on the paid API for now but cheaper options + alternative sources get an options note.

Second round of asks (2026-07-13): mitigate noisy-neighbor risk on the CPX21 (pipeline and live site share 3 vCPU / 4 GB with no priority isolation), fix repeated full reads in the snapshot stage, decide whether Postgres or a decoupled analysis instance is needed now, classify more workload without exhausting LLM budget, and make the data more targeted. Kobe confirmed all three targeting levers (relevance gate, source curation, high-signal prioritization) and three budget levers (merged per-doc calls, confidence tiering, Gemini Batch API).

**Scaling posture (answers the Postgres and decoupling questions):** stay on SQLite; the web box keeps the crawler, analyze engine, API, and UI. None of the 2026-07-09 Postgres revisit triggers are met (multi-host writer, sustained concurrent writer, managed-DB need) — the contention is a CPU/IO-priority problem, not a DB-engine one. **Do not decouple the crawler/engine to its own server:** analyze is the single SQLite writer and several API endpoints (`/movers`, `/entity-posts`, `/review/*`) read the same DB live, so moving the engine off-box forces the multi-host/Postgres jump for no gain — once inference is remote (Phase D), analyze's own CPU is mostly idle network-wait on LLM calls. The only thing that gets its own server is **inference, because it is stateless** (no DB, no data gravity — just a model behind an HTTP endpoint). Phases C and D thus move the two heaviest growth vectors (social ingestion, LLM inference) off the web box; Phase G adds kernel-level priority so the site always wins; Phase H removes the wasted I/O. Escalation path if measurement later demands it: resize CPX21 -> CPX31 first, split analyze + Postgres last — per the standing scale-out order.

Key architectural facts that make this safe:
- Raw store is content-addressed (`data/raw/sha256/`), immutable, idempotent — trivially mergeable across machines via rsync.
- Ingestion tables use natural-key PKs with upserts (`reddit_posts_raw(fullname)`, `x_posts_raw(tweet_id)`, etc.) — conflict-free to merge.
- `docs`/`ai_outputs` use AUTOINCREMENT ids — must be produced on ONE machine only (the server). Analyze never runs locally.
- ETL is skip-based by `ident`; analysis stages queue off `doc_task_state` (no time filter). Widening the window ingests older raw rows as new docs; only those get analyzed. Nothing is reprocessed twice.

Execution order: A → G → H → B → C → I → D → E → J. G and H land with/right after A because the 90-day window immediately raises aggregation load; I gates spend before the backfill drain finishes; J is a steady-state optimization, not a drain prerequisite (the drain is single-digit dollars on flash either way). Each phase is independently shippable. Kobe owns all git actions (no commits/branches by Claude). Repo conventions: todo checklists under `docs/todos/`, dated audit-trail entry per layer per PR, no emojis.

Suggested todo files: `docs/todos/ninety-day-window.md` (A), `docs/todos/box-isolation-and-snapshot-efficiency.md` (G+H), `docs/todos/local-reddit-ingestion.md` (B+C), `docs/todos/targeted-classification.md` (I), `docs/todos/cloud-inference-and-throughput.md` (D+E, with F as a decision note), `docs/todos/llm-call-efficiency.md` (J).

---

## Phase A — Configurable 90-day window

**Python:**
- `analysis/src/common/settings.py`: add `doc_max_age_days: int = 90` (`CIVIC_DOC_MAX_AGE_DAYS`). Comment: must stay >= largest snapshot window (90d).
- `analysis/src/etl/loader.py`:
  - Replace `THIRTY_DAYS_SECONDS` (line 120) with `DEFAULT_MAX_AGE_SECONDS = 90 * 24 * 60 * 60`.
  - `ContentLoader.__init__` takes `max_age_seconds=DEFAULT_MAX_AGE_SECONDS`; the three `is_recent(...)` call sites (news ~316, reddit ~374, x ~429) pass `self.max_age_seconds`. Keep pre-2020/future-date guards unchanged.
  - Bump `ETL_VERSION` to `"etl-v3"` (INVARIANTS.md B1 — comment at loader.py:112-116 explicitly requires this when the 30-day rule changes).
- Constructor call sites (`analysis/src/scheduler/job_runner.py` ~line 83, `analysis/src/api/routers/admin.py` ~line 22): pass `settings.doc_max_age_days * 86400`.

**Go:**
- `ingest/internal/config/config.go`: add `MaxAgeDays int` (`yaml:"max_age_days"`) to `CrawlConfig`, default 90 in `Load()`. Add `max_age_days: 90` to `data/seeds.yaml` with comment (strict YAML decoding requires the struct field to exist).
- `ingest/internal/runner/ingest.go:35-36`: `cutoff := time.Now().AddDate(0, 0, -cfg.Crawl.MaxAgeDays)`.

**Docs:** update "30 days" statements in `docs/ARCHITECTURE_DIAGRAM.md` (~48, 77), `docs/DATABASE_SCHEMA.md` (~152), project `CLAUDE.md`, and INVARIANTS.md B1 wording ("recency-window rule"). Add `CIVIC_DOC_MAX_AGE_DAYS` to `.env.example`.

**Migrations:** none.

**Effect on prod data:** first analyze run after deploy ingests every raw row aged 30–90d already sitting in `articles_raw`/`reddit_posts_raw`/`x_posts_raw` as new etl-v3 docs; stage queues pick them up automatically. This is the "reprocess so data is richer today" ask — no re-crawl needed for already-captured content.

**Tests:** extend `analysis/tests/` loader tests (45d-old doc passes default, fails with 30d override); Go config default/override test. `PYTHONPATH=$PWD python3 -m unittest ...`; `cd ingest && go test ./...`.

**Risk / op note:** the 30–90d backlog becomes pending all at once. Drain rate = `CIVIC_LOADER_BATCH_SIZE` (default 200) × 4 analyze fires/day. Temporarily raise it on the server (settings.py comment already blesses this) while watching Gemini spend.

---

## Phase B — Richer Reddit fetching (pagination + backfill)

**Extractor `ingest/internal/extract/reddit/reddit.go`:**
- Generalize `FetchSubredditPostsPublic` → `FetchListingPage(ctx, sub, listing, tParam, limit, after)` returning `(posts, rawJSON, nextAfter, err)`.
  - URL: `/r/<sub>/<listing>.json?limit=N[&t=<t>][&after=<cursor>]`, listing ∈ {new, top}.
  - Parse `data.after` from the listing envelope; surface HTTP 429 as a typed error.
- Keep `FetchPostCommentBodiesPublic` as-is.

**Runner `ingest/internal/runner/reddit.go`:**
- Paginate per sub following `after` until `max_posts_per_subreddit`, empty page, or empty cursor. Store each page's raw JSON via RawStore (per-page `raw_hash` preserves A6/A7 traceability).
- Pacing: sleep `request_interval` (default 2s) between requests; on 429 sleep 60s, retry once, then skip the sub.
- `--backfill` mode: run both `new` (paginate to ~1000 cap) and `top.json?t=quarter` (~1000, genuine 90-day quality reach) per sub.
- Optional comments (config-gated, **default off**, separate checklist box — cut if scope pressure): for top-N posts per sub, store comments JSON via RawStore and write `reddit_comments_raw` keyed by that blob's hash — exactly the recipe already written in the comment at reddit.go:64-71.

**Config (`config.go` RedditConfig + `data/seeds.yaml`):** `posts_per_request: 100`, `max_posts_per_subreddit: 200`, `request_interval: 2s`, `fetch_comments: false`. Defaults in `Load()`.

**CLI (`ingest/cmd/civic-ingest/main.go`):** `--backfill` bool flag on `redditCmd`. Backfill is one-shot: `./civic-ingest reddit --backfill`.

**Tests:** `httptest`-based — pagination follows/terminates on cursor, `t=quarter` URL, 429 backoff, post cap, config defaults.

**Volume math (one-time backfill):** 12 subs × ~2000 posts pre-dedup ≈ realistically 8–12k posts → est. 5–10k new docs → **~25–50k LLM calls**. Low single-digit dollars on Gemini flash (days to drain); far too slow on a CPU inference box — hence drain on Gemini before the Phase D switch.

**Audit trail:** `docs/audit-trail/ingestion/` entry superseding `2026-04-22-disable-reddit-ingest.md`.

---

## Phase C — Local WSL ingestion + ship to Hetzner

**Design:** local machine runs `civic-ingest reddit` against its own local SQLite + raw store, then pushes: (1) rsync raw blobs, (2) ship a consistent DB snapshot, (3) server-side merge of social ingestion tables. Server's 6-hourly analyze timer picks up merged rows with zero analyze changes.

**Merged tables:** `reddit_posts_raw`, `reddit_comments_raw`, `x_posts_raw`, `x_users_raw` (x future-proofing; X itself stays on Hetzner — its `x_api_budget` spend counter lives in the server DB and must not split-brain). **Excluded:** `pages`/`articles_raw` (server crawls news; `pages` holds live frontier state) and `x_api_budget`.

**New Go subcommand `civic-ingest merge`** (`mergeCmd` in main.go + new `ingest/internal/runner/merge.go`):
- Flags: `--db` (target), `--from` (snapshot), `--dry-run`.
- Preflight: abort loudly on `schema_version` mismatch between target and source.
- `ATTACH` source read-only; per table one short transaction: column-explicit `INSERT ... SELECT ... ON CONFLICT(<natural key>) DO UPDATE SET score=excluded.score, ...` mirroring `upsertRow` semantics in `ingest/internal/runner/dbwrite.go` (so refreshed reddit scores propagate). Go subcommand over ad-hoc shell SQL: testable, single source of truth for upsert semantics, reuses busy_timeout, runs server-side via the existing `docker compose run --rm` pattern.
- `--dry-run`: per-table would-insert counts, no writes.
- Short transactions + existing busy_timeout → safe against live WAL writers; no service stop.

**New sync script `deploy/scripts/sync-ingest-to-server.sh`** (runs locally):
1. `sqlite3 data/civic_lens.db "VACUUM INTO '<tmp>/export.db'"` — consistent point-in-time snapshot under WAL.
2. `rsync -a data/raw/sha256/ deploy@server:/var/lib/civic-lens/data/raw/sha256/` — **blobs before rows** so every merged `raw_hash` resolves (A6/A7).
3. rsync `export.db` → server `data/incoming/`, overwritten each run.
4. `ssh deploy@server 'docker compose run --rm ingest merge --db ... --from ...'`.
5. `--dry-run` passthrough; fail loud on any step.

**Local scheduling:** new `deploy/local/` — systemd **user** units `civic-lens-reddit-local.{service,timer}` (hourly; `Persistent=true` so missed runs fire on WSL start) running `run.sh reddit` + sync; README covering WSL specifics (`systemd=true` in `/etc/wsl.conf`; cron fallback per setup-cron.sh precedent). Hourly volume: 12–24 requests at 2s pacing — polite.

**Migrations:** none.

**Tests:** Go merge tests with two temp DBs — inserts, score-update on conflict, `pages` untouched, dry-run writes nothing, schema-version mismatch aborts. Shellcheck the script.

**Verification:** local `./run.sh reddit` populates `reddit_posts_raw` → `--dry-run` shows counts → real sync → server row count/`MAX(fetched_at)` advances → next analyze fire creates reddit docs and the dashboard reddit tier populates. Litestream unaffected (server DB remains the single analyzed/served primary; merge is just normal writes).

---

## Phase D — Cheap cloud inference tier + Gemini escalation

**Design:** a second small Hetzner server runs Ollama; the analyze pipeline calls it as the primary classifier and escalates to Gemini only when the local result is weak. Inference is the one component that is stateless (no DB, no data gravity), which is exactly why it — and nothing else — gets its own box.

**Inference box (ops, manual Kobe steps in the todo):**
- Hetzner **CAX21** (4 vCPU Ampere ARM, 8 GB, ~7 EUR/mo) in the same region as the web box; resize to CAX31 (~13 EUR/mo) only if measured throughput demands it. ARM is llama.cpp/Ollama's happy path for CPU quants.
- Attach both servers to a **Hetzner private network**; Ollama binds the private IP only (`OLLAMA_HOST=<private-ip>`), cloud firewall blocks the public interface on 11434. No Tailscale/Tunnel needed — private network is simpler, free, and needs no auth-header support.
- `ollama pull nomic-embed-text` (required by narrative embedding mode) + chat model: start `qwen2.5:3b` (current default), evaluate a 7B quant if eval quality justifies the ~2x slowdown. `OLLAMA_NUM_PARALLEL=2`, `OLLAMA_KEEP_ALIVE=-1` (model stays resident between 6-hourly runs).
- Throughput reality check (todo item, measure): CPU inference ~5-15 tok/s; steady state (~hundreds of new docs/day x 4-5 calls) fits a 6-hourly batch cadence, the backfill drain does not — **drain the A/B backlog on Gemini first**, flip after.

**Tiered escalation (the code part):**
- New backend value `CIVIC_LLM_BACKEND=tiered`: `factory.get_llm_client()` returns a new `TieredLLMClient` (new `analysis/src/llm/tiered.py`) composing the existing ollama client (primary) + gemini client (escalation). Same client interface — **engines untouched**.
- Escalation rule: after the local call, if the parsed result's `confidence` field < `CIVIC_LLM_ESCALATION_THRESHOLD` (new setting, e.g. default 0.6) **or** the local call fails schema validation/errors, re-run the same prompt on Gemini and return that result. The saved `ai_outputs` row carries whichever `model_id` actually produced it — the per-row contract (confidence/model_id/prompt_version) makes tiering fully auditable for free, and the review queue can compare tiers.
- Log an escalation counter per run (docs escalated / total) so the Gemini bill maps to a visible number; alert-worthy if escalation rate stays > ~30% (means the local model is too weak — bigger quant or stay on Gemini).
- Verify from **inside** the analyze container that the private-network IP is reachable (bridge → host NAT → private net; document host-networking fallback if not).
- Config: `CIVIC_OLLAMA_HOST=http://<private-ip>:11434`, `CIVIC_OLLAMA_TIMEOUT=300`, `CIVIC_LLM_BACKEND=tiered` in `/etc/civic-lens.env`.

**Quality gate before flipping prod:** golden-set eval (`analysis/src/reporting/review.py`, `ai_output_evals`) on the tiered backend vs the Gemini baseline; acceptance threshold in the todo (e.g. no task drops >5 points). The threshold knob gives a dial: raising `CIVIC_LLM_ESCALATION_THRESHOLD` trades money for quality without redeploying.

**Fallback:** manual rollback = flip `CIVIC_LLM_BACKEND=gemini`. No additional auto-failover — escalation-on-error already covers the inference box being down (every call escalates; bill rises but pipeline completes; the escalation-rate log makes it obvious).

**Tests:** TieredLLMClient unit tests with fake clients — low confidence escalates, high confidence doesn't, local error/schema-failure escalates, model_id passthrough correct.

**Docs:** update `docs/todos/containerization.md` "self-hosted inference doesn't pencil out" note (a 7 EUR CAX21 + escalation changes that math); `.env.example`; audit-trail entries in `infra/` and `analysis/`.

---

## Phase E — Analyze throughput (minimal, evidence-driven)

**Bounded LLM concurrency:**
- `settings.py`: `llm_max_concurrency: int = 1` (`CIVIC_LLM_MAX_CONCURRENCY`; default 1 = exactly current behavior, zero-risk rollout).
- One small helper (e.g. `_parallel_docs(docs, analyze_fn, save_fn)` in job_runner): `ThreadPoolExecutor(max_workers=N)` runs the LLM calls; **all SQLite writes stay on the main thread** as futures complete. Apply to the five per-doc LLM stages (text, bot, targets, propaganda, claims). Prompts/models/outputs identical — throughput only, quality untouched.
- Checklist item: verify the LLM client singleton holds no mutable per-call state (requests-per-call, should be fine).
- Gains: Gemini immediate (network-bound); Ollama needs `OLLAMA_NUM_PARALLEL` on the inference box and tops out ~2 on a CPU host.

**Catch-up batch size:** no code — documented op step `CIVIC_LOADER_BATCH_SIZE=1000` during backfill drain.

**Aggregators/snapshots:** measure `save_snapshots()` at 90d volume first. If it presses the 120s `_SNAPSHOTS_RESERVE_SECONDS` (job_runner.py ~907), raise the constant; only add a composite index (new migration) if measurement demands it. Check overlap with existing `docs/todos/backend-aggregator-audit.md` first.

**Explicitly cut (overbuilt for this volume):** cross-doc prompt batching, completion caching, async rewrite, multiprocess workers.

**Tests:** parallel helper unit tests — results identical to sequential with a fake analyzer, per-doc exception isolation preserved, concurrency=1 path unchanged. Verification: wall-clock a ~200-doc stage at concurrency 1 vs 4; identical row counts and confidence/model_id/prompt_version population.

---

## Phase F — X cost + alternative sources (options note only, no implementation)

Short decision note (audit-trail infra note or PR description; no speculative todo boxes):
- **Cheaper X now, config-only:** drop/halve `political_queries` in seeds.yaml (topic search ≈ $24/mo; substitutable by Reddit) and keep officials timelines (≈ $22/mo, the unique value) — roughly halves spend with a YAML edit; optionally stretch the x timer daily → every 2–3 days. Free tier is write-only (useless). NextToken pagination not worth building at the 10-tweet floor.
- **X backfill is impossible on this tier** — `/2/tweets/search/recent` is 7-day capped; 90-day X coverage grows forward only.
- **Bluesky is the strongest free addition** (public AppView search, no auth, + Jetstream firehose); fits the x-extractor pattern (new extractor + `bluesky_posts_raw` migration + loader branch + source_type) but is its own future initiative. Mastodon: free but low political density, lower priority.

---

## Phase G — Box isolation: site wins under contention (compose + read-path hardening)

Facts driving this: `docker-compose.yml` caps are ceilings only — `api` (0.8 cpu/512m), `analyze` (0.9/1g), `ingest` (0.8/512m) can all be runnable at once on 3 vCPU with nothing prioritizing the live site. systemd units deliberately delegate caps to compose (`civic-lens-analyze.service:16`), so the fix belongs in compose. The API is NOT purely cache-served: `/movers`, `/entity-posts`, `/review/*`, `/health` hit SQLite live, and neither they nor the aggregators set `busy_timeout`.

**CPU priority via weights (compose-only change):**
- `docker-compose.yml`: add `cpu_shares: 2048` to `api` and `caddy`; `cpu_shares: 256` to `analyze`, `ingest` (the `jobs` profile services). Keep existing `cpus:` ceilings as backstops. CFS weights are exactly the right primitive: jobs get the whole idle box when the site is quiet, and get preempted proportionally the moment real requests arrive. Zero code, one file.
- Add `mem_reservation: 256m` on `api` so job memory bursts can't push it to swap-thrash territory.
- **IO isolation note (documented, not built):** `blkio_config` weights require the BFQ scheduler and are unreliable on cloud virtio disks — do not add them. The IO answer is Phase H (stop re-reading everything), not kernel IO weighting.

**Read-path hardening (small Python changes):**
- `analysis/src/reporting/aggregators/base.py` `get_connection()` (~132-140): set `PRAGMA busy_timeout = 5000` (it currently sets only `foreign_keys`), matching `loader.py`'s `SQLITE_BUSY_TIMEOUT_MS`.
- Same pragma on the API's live-read connections: `api/routers/data.py` (`/movers`, `/entity-posts` direct connects), `reporting/review.py` (`ReviewService`), `api/routers/health.py`. Under WAL, readers never block on the analyze writer, but `SQLITE_BUSY` on connection open / checkpoint contention is real without a timeout.

**Measurement + escalation triggers (documented in the todo, checked after A lands):**
- Baseline: `curl -w '%{time_total}'` against `/api/sentiment?window=90d` (cache path) and `/api/movers` (DB path) during an analyze run vs idle; log `save_snapshots()` wall-clock (Phase E already requires timing it).
- Triggers, in order: sustained DB-path p95 > ~1s during runs, or analyze wall-clock pressing the 2h `TimeoutStartSec` → **resize CPX21 -> CPX31** (doubles vCPU, ~minutes of downtime, no architecture change). Only if a bigger box still contends → move the `analyze` one-shot to a second small VPS reading a Litestream replica — and only then does the Postgres conversation reopen (multi-host trigger). Do not build any of that now.

**Migrations:** none. **Tests:** none beyond existing (compose is config); verify with `docker compose config` and the measurement steps above.

**Audit trail:** `docs/audit-trail/infra/` entry updating the 2026-07-09 docker-compose-stack entry's resource-cap story (ceilings → ceilings + weights).

---

## Phase H — Snapshot efficiency: stop recomputing the world every 6 hours

Facts driving this: `save_snapshots()` (`job_runner.py:796-892`) recomputes 5 aggregators × 4 windows from scratch every run. Every aggregator query goes through the `ai_outputs_latest` view, whose correlated `MAX(output_id)` subquery (`data/migrations/023_ai_outputs_label.sql:37-42`) is re-materialized on each of the ~20 computations; each aggregator opens its own connection; `docs(source_type)` has no index. At 90 days this is the dominant repeated-read cost.

**Measure first** (Phase E already mandates timing `save_snapshots()` at 90d volume), then apply in this order, stopping when the number is comfortable (well under the 120s `_SNAPSHOTS_RESERVE_SECONDS`):

1. **Materialize latest outputs once per run:** at the top of `save_snapshots()`, create `TEMP TABLE latest_outputs AS SELECT ... FROM ai_outputs_latest` (optionally pre-joined with the doc columns aggregators need), on a single connection passed to all aggregators for the run; aggregators query the temp table instead of the view. `aggregators/base.py` gains an optional injected-connection/table-name path; default behavior (own connection, view) unchanged so the API cache-miss fallback path keeps working untouched. This collapses ~20 view re-materializations into 1.
2. **Migration — composite index** `idx_docs_source_published ON docs(source_type, published_at)`: aggregators filter/branch on `source_type` constantly (`base.py:259`) with no supporting index.
3. Only if still slow: raise `_SNAPSHOTS_RESERVE_SECONDS`; consider per-window incremental caching. Explicitly cut for now: full incremental aggregation (recompute-from-scratch is the simplest correct design and stays fine once reads are cheap).

**Cross-check `docs/todos/backend-aggregator-audit.md`** before starting — fold overlapping boxes in rather than duplicating.

**Tests:** aggregator results identical via temp-table path vs view path on a fixture DB; migration applies cleanly. **Audit trail:** `analysis/` entry.

---

## Phase I — Targeted classification: relevance gate + queue priority + source curation

Goal: LLM budget goes to politically relevant, high-signal docs first; junk stops costing 4-5 calls per doc. Builds on what exists: the ETL already has a binary keyword gate (`is_us_political_content`, `loader.py:129`) and `get_unprocessed_docs` (`loader.py:465`) has **no ORDER BY** — the queue is arbitrary rowid order today.

**1. Relevance score at ETL (replaces the binary gate's output, keeps its logic):**
- `loader.py`: extend `is_us_political_content` with a companion `relevance_score(text, title, url) -> float` — normalized keyword-hit density over the existing `US_POLITICAL_KEYWORDS` (distinct keywords matched, weighted by title hits), 0.0-1.0. Deterministic, no LLM (Rule 5: code answers this). Binary gate stays as the hard floor; the score adds ordering/thresholding on top.
- **Migration:** `ALTER TABLE docs ADD COLUMN relevance_score REAL` + backfill `UPDATE` for existing docs is NOT possible deterministically without re-reading raw text — instead compute at doc-creation time only and treat NULL as "pre-scoring doc, score 0.5 neutral". Bump `ETL_VERSION` is already happening in Phase A (etl-v3) — fold this in so it's one re-ingest, not two.
- `settings.py`: `relevance_threshold: float = 0.0` (`CIVIC_RELEVANCE_THRESHOLD`; default 0.0 = current behavior, zero-risk rollout). LLM stages skip docs below threshold by adding `AND COALESCE(d.relevance_score, 0.5) >= ?` in `get_unprocessed_docs`; skipped docs get a `doc_task_state` row with status `skipped_low_relevance` so they don't re-queue forever and remain auditable (never silently dropped — fail-loud invariant).

**2. Queue priority (one query change):**
- `get_unprocessed_docs`: add `ORDER BY COALESCE(d.relevance_score, 0.5) DESC, d.published_at DESC` before `LIMIT`. Engagement (reddit score/num_comments) lives in `metadata_json`, not a column — do NOT add JSON-extract ordering now; relevance + recency is the 90% answer. When a capped run ends, budget went to the most relevant, freshest docs.

**3. Source curation (config + ops, no code):**
- Todo checklist item for Kobe: prune `data/seeds.yaml` — drop RSS feeds/subreddits whose docs consistently score low. Supporting query documented in the todo (avg relevance_score + doc count by source over 30d, read-only sqlite) so pruning is evidence-based, not vibes.

**Tests:** relevance_score unit tests (pure function — political text high, marginal text low, title weighting); get_unprocessed_docs threshold + ordering tests on a fixture DB; skipped docs get state rows. **Docs:** labeling note in the todo — thresholding changes the sample composition, so `docs/INVARIANTS.md` sampling language should be checked (still "sampled discourse", now explicitly relevance-filtered).

**Audit trail:** `analysis/` entry.

---

## Phase J — LLM call efficiency: merged per-doc calls, per-task routing, batch API (deferred)

Steady-state cost levers, sequenced after D (needs the golden-set eval loop to exist as a quality gate). All three confirmed by Kobe; honest sequencing below.

**1. Merged per-doc call (the big one, ~3-4x fewer calls):**
- Today each social doc costs separate calls: text (sentiment+favorability), targets, propaganda, claims (+bot where LLM-assisted). Merge the compatible per-doc tasks into ONE structured call returning a composite object; claims stays separate (different output cardinality and failure modes).
- `analysis/src/llm/schemas.py`: add a composite schema composing the existing per-task sub-schemas (reuse, don't redefine). `engine/prompts.py`: new merged prompt + new prompt-version constant (per the AI-output contract). The engine fans the composite result out into the SAME per-task `ai_outputs` rows via `loader.save_ai_output` — one row per task, each with its own confidence/model_id/prompt_version, so downstream aggregators, the review queue, and `ai_outputs_latest` are completely untouched.
- Config-gated: `CIVIC_LLM_MERGED_TASKS=false` default; both paths share the fan-out writer. Quality gate before flipping: golden-set eval merged-vs-separate (same infra as Phase D's gate). Risk to watch: long docs + composite schema → truncation; cap doc text the same way the worst-case existing prompt does.
- Works identically on Gemini and Ollama (both behind `get_llm_client()` with JSON-schema enforcement — no backend special-casing, per repo convention).

**2. Per-task backend overrides (layered on Phase D's tiered client):**
- Confidence-based escalation is already built in Phase D (`TieredLLMClient`). What the golden-set eval may additionally show is a task the local model is *systematically* bad at (e.g. claims) — where always-escalating wastes a local call per doc. Then add a static task→backend map: `settings.py` `llm_backend_overrides: dict[str,str]` (`CIVIC_LLM_BACKEND_OVERRIDES`, e.g. `{"claims":"gemini"}`), resolved in the tiered client so those tasks go straight to Gemini. Deterministic, trivially auditable; build only when the eval data shows that shape.

**3. Gemini Batch API — deferred with an explicit trigger (pushback):**
- The 50% batch discount is real, but the one-time backfill drain is single-digit dollars total and steady state post-tiering should be near zero Gemini spend (escalations only) — building an async submit/poll/collect path saves a few dollars. **Trigger to build it:** sustained Gemini spend > ~$20/mo (e.g. the local-model eval fails and Gemini stays primary at 90d volume). Until then it's a decision note in the todo, not code. If triggered: standalone drain script (`analysis/scripts/batch_drain.py`) writing through `loader.save_ai_output`, never wired into the 6-hourly steady-state path (async hours-long turnaround fights the run cadence).

**Tests:** composite schema round-trip + fan-out writer (one call → N ai_outputs rows, correct per-task prompt_version); routing map resolution; merged-off path byte-identical to today. **Audit trail:** `analysis/` entry.

---

## Verification (end-to-end)

1. **Phase A:** unit tests pass (Python + Go, per commands above); `./run.sh analyze --tasks etl` locally against a copy of raw data shows 30–90d docs land as etl-v3. On prod: pre-count 30–90d raw rows (read-only sqlite), post-deploy analyze run shows docs count rise and 90d dashboard windows populate; time `save_snapshots()`.
2. **Phase B:** go tests; local `./civic-ingest reddit --backfill` fetches ~1–2k posts/sub without 429s.
3. **Phase C:** dry-run → real sync → server counts advance → next analyze fire → reddit data visible in UI.
4. **Phase D:** golden-set eval of tiered backend meets threshold before env flip; post-flip sampled review of fresh ai_outputs plus escalation-rate log sanity check (roughly matches eval expectations).
5. **Phase E:** concurrency 1-vs-4 wall-clock comparison with identical outputs.
6. **Phase G:** `docker compose config` validates; during an analyze run, `curl -w '%{time_total}'` on `/api/movers` (DB path) stays near idle baseline; no SQLITE_BUSY errors in api logs.
7. **Phase H:** `save_snapshots()` wall-clock before/after at 90d volume; aggregator outputs byte-identical via temp-table vs view path.
8. **Phase I:** with `CIVIC_RELEVANCE_THRESHOLD=0` outputs unchanged; raised threshold shows `skipped_low_relevance` state rows and high-relevance docs processed first in a capped run.
9. **Phase J:** golden-set eval merged-vs-separate meets threshold; one merged call produces N per-task ai_outputs rows with correct confidence/model_id/prompt_version; `CIVIC_LLM_MERGED_TASKS=false` path unchanged.

## Critical files

- `analysis/src/etl/loader.py` (window constant, `is_recent`, `ETL_VERSION`)
- `analysis/src/common/settings.py` (`doc_max_age_days`, `llm_max_concurrency`)
- `ingest/internal/runner/ingest.go` (RSS cutoff), `ingest/internal/runner/reddit.go` + `ingest/internal/extract/reddit/reddit.go` (pagination/backfill/pacing)
- `ingest/internal/config/config.go` + `data/seeds.yaml` (new config fields)
- `ingest/cmd/civic-ingest/main.go` (`--backfill`, `merge` subcommand), new `ingest/internal/runner/merge.go` (reuse `upsertRow` semantics from `dbwrite.go`)
- `analysis/src/scheduler/job_runner.py` (loader construction, concurrency helper, snapshot reserve, temp-table materialization in `save_snapshots`)
- `docker-compose.yml` (cpu_shares weights, mem_reservation)
- `analysis/src/reporting/aggregators/base.py` (busy_timeout, injected-connection path), `analysis/src/api/routers/data.py` + `analysis/src/reporting/review.py` (busy_timeout on live reads)
- `analysis/src/llm/schemas.py` + `analysis/src/engine/prompts.py` + `analysis/src/llm/factory.py` (composite schema, merged prompt version, tiered backend wiring), new `analysis/src/llm/tiered.py` (`TieredLLMClient`, `CIVIC_LLM_ESCALATION_THRESHOLD`)
- New migration: `idx_docs_source_published` composite index; `docs.relevance_score` column
- New: `deploy/scripts/sync-ingest-to-server.sh`, `deploy/local/` units + README
