# 2026-07-09 — Crawler remediation: frontier integrity, provenance, politeness

Closes the confirmed ingestion findings from the same-day adversarial review
(`2026-07-09-adversarial-review.md`, I-1 through I-12). The crawler now keeps
the frontier key immutable through a page's lifecycle, refuses to let a fetched
page dictate another publisher's records, never persists rows without a raw-blob
hash, counts X API spend honestly, and enforces politeness across redirects and
robots.txt edge cases. Invariant A4 was rewritten to describe the failure
accounting the system actually keeps rather than a `fetch_event` ledger that
never existed.

## What shipped

### Frontier integrity (I-1, I-1b, I-7)
- `runner/crawl.go` no longer overwrites `page.URLCanon` with the page-declared
  `<link rel="canonical">`. The frontier key that `MarkDone` matches on stays
  the URL that was claimed, so a fetched page can no longer be stranded INFLIGHT
  and refetched forever.
- `frontier.updatePageState` now guards on the claim: `WHERE url_canon = ? AND
  state = INFLIGHT AND inflight_at = <claim ts>`, and returns an error when the
  UPDATE matches 0 rows. A worker whose row was recovered and re-claimed by
  another worker can no longer clobber the new claim, and a mismatched key is a
  loud error instead of a silent no-op. `RecoverStale`'s young-row skip is
  documented as defense-in-depth on top of this guard.

### Provenance and traceability (I-2, I-3, I-6, I-11)
- `runner/article_writer.go` validates the declared canonical
  (`util.CanonicalizeURL` + same registrable domain as the fetched page via the
  public suffix list) before keying `articles_raw` off it; otherwise it falls
  back to the frontier URL. Cross-outlet content poisoning via `og:url` is
  closed. The FK placeholder `pages` row it inserts is now QUEUED, not DONE, so
  a real same-publisher canonical stays crawlable instead of being silently
  blocked.
- `runner/x.go`, `runner/reddit.go`, `runner/x_officials.go` check
  `RawStore.Store` errors and skip the item (logging, counting a failure)
  instead of proceeding with an empty hash — removing both the `hash[:8]` panic
  and the empty-`raw_hash` rows.
- `runner/dbwrite.go` gains `upsertRowOnConflict`; the X topic-query insert path
  uses it with `ON CONFLICT(tweet_id) DO UPDATE` that omits `is_official_tier`,
  so a topic match on an officials-pass tweet preserves the tier flag instead of
  resetting it (INSERT OR REPLACE used to erase it). The `is_official_tier`
  column is retained pending the data-contract D-3/D-4 decision on a separate
  branch.
- `runner/reddit.go` no longer merges comment bodies into
  `reddit_posts_raw.body`; that text had no covering raw-blob hash. A comment on
  the (currently disabled) Reddit path notes that comment collection must store
  the comments-endpoint JSON via `RawStore` and write `reddit_comments_raw`
  before it is re-enabled.

### Budget accounting (I-5a, I-5b)
- `runner/x_budget.go` rounds each call's cost UP to the next whole cent
  (`ceilDiv`) instead of truncating fractional cents, and applies the increment
  with a relative `SET estimated_cents = estimated_cents + ?` statement, then
  reloads the authoritative totals. Overlapping runs can no longer clobber each
  other's increments, and the ceiling can no longer drift below real spend.

### Politeness (I-8, I-9, I-10)
- `robots/robots.go` was reworked: consecutive `User-agent` lines share one
  directive group; UA matching is case-insensitive with most-specific-block-
  wins; and path evaluation uses longest-match with Allow winning ties, so
  `Allow: /` no longer nullifies every `Disallow`. The non-200 robots.txt path
  now closes the response body.
- `httpclient/client.go` takes a rate-limit token against the redirect target's
  domain inside `CheckRedirect`. Per-hop robots re-checks on cross-domain
  redirects are explicitly out of scope for this pass (documented follow-up).

### Durability and config hygiene (I-12a, I-12b)
- `storage/rawstore/rawstore.go` fsyncs the temp file before renaming (so a
  referenced hash can never point at a truncated file) and sweeps orphaned
  `.tmp` files on startup.
- `config/config.go` decodes seeds.yaml with `KnownFields(true)`, so a
  misspelled key fails loudly instead of silently reverting to a default.

### Invariant (I-4)
- `docs/INVARIANTS.md` A4 was rewritten. Decision: the lighter, honest option —
  describe the failure accounting the system keeps (`pages.last_error` + retries
  + state transitions; raw blobs for API calls) rather than build a
  `fetch_events` table. A per-attempt ledger was not worth adding: it would
  require a new migration plus wiring into every fetch path for an audit surface
  nothing currently consumes.

## Why

The adversarial review traced concrete failing paths: pages stuck INFLIGHT and
refetched forever (I-1), cross-outlet poisoning (I-2), panics / untraceable rows
on disk-full (I-3), budget drift releasing calls past the ceiling (I-5), lost
provenance flags (I-6), and robots.txt shapes that let disallowed paths through
(I-8) — all violations of the Part-A invariants for a system whose README
promises politeness and end-to-end traceability.

## Tests

- `frontier`: `TestMarkDoneUsesFrontierKey`, `TestMarkDoneWrongKeyIsLoud`,
  `TestMarkDoneStaleReclaimGuard`.
- `runner`: `TestWriteFromMeta_HostileCanonicalDoesNotOverwriteOtherOutlet`,
  `TestWriteFromMeta_SameDomainCanonicalAccepted`,
  `TestUpsertRowOnConflict_PreservesOfficialTier`,
  `TestUpsertRowOnConflict_NewRowGetsDefaultTier`,
  `TestSafePrefixHandlesShortString`; updated `TestXBudgetTracker_RecordAccumulates`
  for ceil rounding.
- `robots`: table-driven `TestCheckPath_*` (longest-match, tie-to-allow, shared
  group, case-insensitive UA, most-specific-wins, empty-disallow).
- `rawstore`: `TestStoreRoundTrip`, `TestStoreErrorReturnsNoHash`,
  `TestNewSweepsOrphanedTempFiles`.
- `config`: `TestLoad_StrictRejectsUnknownKey`, `TestLoad_KnownKeysStillParse`.

## Follow-ups

- Potential improvement: an append-only `fetch_events` ledger (url, ts, status,
  outcome, error) recorded at each crawl fetch outcome would give per-attempt
  fetch history — the audit surface A4 originally described. Deferred for now
  (nothing consumes it yet); A4 was rewritten to match what the system keeps
  today. Revisit if retry forensics or per-domain failure dashboards are needed.
- Per-hop robots.txt re-check on cross-domain redirects (I-10 scoped out here).
- `is_official_tier` retention/removal rides on the data-contract D-3/D-4
  decision on its own branch.
- Reddit comment traceability (store comments JSON + `reddit_comments_raw`)
  before Reddit ingestion is re-enabled.
