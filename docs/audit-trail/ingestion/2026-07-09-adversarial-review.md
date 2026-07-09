# 2026-07-09 — Adversarial review: ingestion layer

Point-in-time adversarial review of `ingest/` (frontier, fetchers, extractors, raw storage, X budget) against the Part-A invariants in `docs/INVARIANTS.md`. Findings only — no fixes shipped in this entry. Method: full-code adversarial pass by a review agent, with every HIGH finding independently re-verified against the source by a second pass before being recorded here. Companion entries of the same date: `../analysis/2026-07-09-adversarial-review.md`, `../ingestion/2026-07-09-adversarial-review-data-layer.md`, `../ui/2026-07-09-adversarial-review.md`.

Severity: HIGH = invariant violation / data loss; MEDIUM = correctness under realistic conditions; LOW = robustness. CONFIRMED = failing path fully traced; SUSPECTED = plausible, not fully traced.

## Findings

### I-1 HIGH CONFIRMED — HTML canonical URL mutation breaks MarkDone; pages refetch forever

`ingest/internal/runner/crawl.go:226-228`: `extractAndEnqueue` overwrites `page.URLCanon` with the page-declared `<link rel="canonical">` value (never passed through `util.CanonicalizeURL`). `processPage` (crawl.go:141) then calls `MarkDone(ctx, page)`, whose `UPDATE pages ... WHERE url_canon = ?` (frontier.go:168) matches zero rows without error whenever the declared canonical differs from the frontier key — trailing slash, scheme, tracking params. The fetched row stays INFLIGHT, `RecoverStale` requeues it after `stale_inflight_age`, and the page is refetched and re-stored on every run, forever (violates A3; burns politeness budget and disk). If the declared canonical happens to equal a *different* queued row's key, that never-fetched row is marked DONE instead — silent data loss. Fix shape: capture the frontier key before mutation (or pass the original key to MarkDone), and canonicalize + same-domain-validate declared canonicals.

### I-2 HIGH CONFIRMED — Crawled pages control articles_raw primary keys via canonical/og:url

`ingest/internal/runner/article_writer.go:64-67`: `WriteFromMeta` prefers `meta.CanonicalURL` unvalidated as `articles_raw.url_canon`; the batched flush (article_writer.go:159-186) upserts `ON CONFLICT(url_canon) DO UPDATE SET ... raw_hash = excluded.raw_hash` and synthesizes a DONE `pages` row for that URL. Any crawled page declaring `og:url = https://<other-outlet>/<article>` overwrites that article's title/raw_hash with its own content and permanently blocks the real URL from being crawled (the synthetic DONE row makes `PushLinks`'s `INSERT OR IGNORE` a no-op). Cross-outlet content poisoning from a single hostile or misconfigured page.

### I-3 HIGH CONFIRMED — RawStore.Store errors discarded; panic or untraceable rows

`ingest/internal/runner/x.go:114-117`, `reddit.go:54-56`, `x_officials.go:116`: `hash, _ := RawStore.Store(...)` ignores the error; on failure `hash == ""` and the next line's `hash[:8]` panics (X/Reddit paths), or the officials path proceeds to insert rows with `raw_hash = ""` — untraceable records violating A6/A7 (the column's NOT NULL does not reject empty string). A disk-full during a run crashes the batch or corrupts traceability.

### I-4 HIGH CONFIRMED — Invariant A4's fetch_event ledger does not exist

`docs/INVARIANTS.md` A4 requires every fetch attempt to record a `fetch_event`. No migration creates such a table and no Go code references one (zero grep hits). Only the last failure per page survives (`pages.last_error`); per-attempt history for retries, robots fetches, seed fetches, and all Reddit/X API calls is never recorded. The invariant should be implemented or rewritten to match reality — as written it promises an audit surface the system does not have.

### I-5 MEDIUM CONFIRMED — X budget undercounts: integer truncation + lost updates

`ingest/internal/runner/x_budget.go:88-106`: per-call cost `posts*centsPerPost/centsConversionUnit` truncates fractional cents (5 tweets records 2c against a true 2.5c; ~$3-4/month untracked at current officials cadence), and `Record` writes absolute in-memory totals (`SET estimated_cents = ?`) loaded at tracker construction, so overlapping `civic-ingest x` runs overwrite each other's increments. Both drift `estimated_cents` below real spend, so `OverBudget()` releases queries past the configured ceiling.

### I-6 MEDIUM CONFIRMED — INSERT OR REPLACE erases is_official_tier on topic-query overlap

`ingest/internal/runner/x.go:178-197` vs `x_officials.go:234-253`: the topic-query insert path uses `INSERT OR REPLACE` without `is_official_tier`, which deletes and re-inserts the row with column defaults. An official's tweet ingested by the officials pass and matched minutes later by a topic query in the same run loses its `is_official_tier=1` flag (migration 018's purpose) — downstream tier routing silently degrades on exactly the high-signal overlap rows.

### I-7 MEDIUM CONFIRMED — Frontier transitions lack claim guards; requeue-stale can double-fetch

`ingest/internal/frontier/frontier.go:144-170`: `MarkDone`/`MarkFailed` UPDATE unconditionally (no `AND state = 1 AND inflight_at = <claim time>`), and `RecoverStale` requeues rows whose fetch is still genuinely in progress (operator-tunable `--stale`). A recovered row can be claimed by worker B while worker A is mid-fetch; A's completion then clobbers B's row state (A3 exclusivity broken in effect: double fetch, retry-counter corruption, DONE rows flipped back to QUEUED).

### I-8 MEDIUM CONFIRMED — robots.txt parser: Allow-first short-circuit, group splitting, case-sensitive UA match

`ingest/internal/robots/robots.go:117-131, 141-171`: (a) any prefix-matching `Allow` returns immediately before `Disallow` is consulted, so `Allow: /` nullifies every Disallow in the group; (b) consecutive `User-agent` lines each open a new empty rule instead of sharing a group; (c) UA matching is case-sensitive substring with first-block-wins. Net effect: disallowed paths get crawled on common real-world robots.txt shapes — a compliance risk for a crawler whose README promises politeness.

### I-9 MEDIUM CONFIRMED — Response body leak on non-200 robots.txt

`ingest/internal/robots/robots.go:76-84`: the early return for `err != nil || resp.StatusCode != 200` fires before `defer resp.Body.Close()` is registered and never closes the body on the non-200 path. robots.txt 404s are the common case; connections/fds accumulate over long crawls.

### I-10 MEDIUM CONFIRMED — Redirects bypass the per-domain token bucket and robots

`ingest/internal/httpclient/client.go:46-56, 93-109`: rate-limit tokens are taken once for the original domain; `http.Client` then follows up to `MaxRedirects` hops with SSRF validation only — no token for the target domain, no robots re-check. N source domains redirecting to one host multiply its request rate N-fold past the configured limit.

### I-11 MEDIUM CONFIRMED — Reddit comment text persisted without a raw blob

`ingest/internal/runner/reddit.go:69-77`: comment bodies fetched from the comments endpoint are merged into `reddit_posts_raw.body`, but that endpoint's raw JSON is discarded — the row's `raw_hash` points at the listing JSON, which lacks the comments. A6/A7 traceability broken for all persisted comment text; `reddit_comments_raw` (migration 001) is written by no code path. (Reddit ingestion is currently disabled per the 2026-04-22 infra entry, which lowers urgency, not correctness.)

### I-12 LOW SUSPECTED — Raw-file writes not crash-durable; config typos silently ignored

`rawstore.go:49-58` uses WriteFile+Rename without fsync (post-power-loss, a referenced hash can point at a truncated file — would violate A5; OS-dependent, hence SUSPECTED) and never cleans orphaned `.tmp` files. Separately (CONFIRMED, empirically): `config.go` parses seeds.yaml without strict-field mode, so a misspelled key like `max_concurency:` silently falls back to defaults.

## What held up

Frontier claiming itself (atomic `UPDATE ... RETURNING`), WAL/busy-timeout setup, migration versioning discipline, `util.CanonicalizeURL` determinism, and `safehost.go`'s SSRF checks (incl. IPv4-mapped IPv6) all withstood the pass; migration 013's CHECK range matches the state constants.

## Recommended fix order

I-1, I-3, I-2 first (each is small and localized; together they close the invariant-A3/A6 violations), then I-6 (switch to UPSERT preserving the flag), I-5 (integer-cents rounding up + read-modify-write in one SQL statement), I-8/I-9/I-10 (politeness correctness), I-7 (guarded transitions), I-11/I-12 with the fetch-event decision from I-4.
