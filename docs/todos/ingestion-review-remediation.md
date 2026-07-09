# Ingestion review remediation

Fixes for the confirmed findings in `docs/audit-trail/ingestion/2026-07-09-adversarial-review.md` (IDs referenced below). Ordered in waves: wave 1 closes the invariant violations and is small + localized; later waves are correctness hardening. Each wave is PR-sized and gets its own audit-trail entry per convention.

## Wave 1 — invariant violations (A3/A5/A6)

- [ ] **I-1: stop mutating the frontier key.** `ingest/internal/runner/crawl.go:226-228` — `extractAndEnqueue` must not overwrite `page.URLCanon`; pass the declared canonical to `WriteFromMeta` via a separate field/argument so `MarkDone` (crawl.go:141) still updates the row that was claimed. Regression test: process a page whose `<link rel=canonical>` differs from its frontier key; assert the row transitions INFLIGHT -> DONE (today it stays INFLIGHT).
- [ ] **I-1b: make MarkDone/MarkFailed loud on zero rows.** `frontier.go:144-170` — return an error (or at least log) when the UPDATE matches 0 rows, so this class of bug can never be silent again.
- [ ] **I-3: check RawStore.Store errors.** `runner/x.go:114`, `runner/reddit.go:54`, `runner/x_officials.go:116` — on error: skip the batch item, log, count a failure; never proceed with `hash == ""`. Removes both the `hash[:8]` panic and the empty-raw_hash rows. Regression test: Store returns error -> no DB row inserted, no panic.
- [ ] **I-2: validate declared canonical URLs.** `runner/article_writer.go:64-67` — run `meta.CanonicalURL` through `util.CanonicalizeURL` and accept it only when same-registrable-domain as the fetched page; otherwise fall back to the frontier key. Also stop synthesizing DONE `pages` rows for URLs never fetched (or mark them with a distinct state so `PushLinks` can still queue the real URL). Test: hostile `og:url` pointing at another domain does not overwrite that domain's `articles_raw` row.
- [ ] **I-4: decide the fetch_event question.** Either implement a minimal `fetch_events` table (append-only: url, ts, status, err) or rewrite INVARIANTS.md A4 to describe what the system actually records (`pages.last_error` + retries). Do not leave the invariant claiming an audit surface that does not exist. This is a scoping decision — make it consciously in the PR description.

## Wave 2 — budget + provenance

- [ ] **I-5a: fix budget truncation.** `runner/x_budget.go:88-106` — compute cost in tenths of cents (or round up per call). Assert: 5 posts at $0.005 records 3 cents (ceil) or 25 decicents, never 2 cents.
- [ ] **I-5b: fix budget lost updates.** Replace the in-memory absolute write (`SET estimated_cents = ?`) with an atomic relative update (`SET estimated_cents = estimated_cents + ?`) so overlapping runs cannot clobber each other.
- [ ] **I-6: preserve is_official_tier on topic-query inserts.** `runner/x.go:178-197` — replace `INSERT OR REPLACE` with `INSERT ... ON CONFLICT DO UPDATE` that does not touch `is_official_tier` (coordinate with the D-3/D-4 decision in `data-contract-remediation.md` — if the column is dropped instead, this item collapses into that one).

## Wave 3 — politeness correctness

- [ ] **I-8: robots.txt parser fixes.** `robots/robots.go:117-131, 141-171` — (a) evaluate longest-match (or at minimum check Disallow before short-circuiting on Allow), (b) share directives across consecutive `User-agent` lines in one group, (c) case-insensitive UA matching with most-specific-block-wins. Table-driven tests with the `Allow: /` + `Disallow: /private/` shape.
- [ ] **I-9: close the robots response body on non-200.** `robots/robots.go:76-84`.
- [ ] **I-10: rate-limit + robots-check redirect targets.** `httpclient/client.go:46-56, 93-109` — take a token for the redirect target's domain inside `CheckRedirect` (and decide whether robots re-check on cross-domain redirects is in scope; document if not).
- [ ] **I-7: guard frontier transitions.** `frontier.go` — add `AND state = 1 AND inflight_at = ?` (the claim timestamp) to MarkDone/MarkFailed so a stale worker cannot clobber a re-claimed row; make `RecoverStale` skip rows younger than the fetch timeout.

## Wave 4 — cleanup

- [ ] **I-11: Reddit comment traceability.** Store the comments-endpoint raw JSON and reference its hash, or stop merging comment bodies into `reddit_posts_raw.body`. Low urgency while Reddit ingestion stays disabled — decide before re-enabling it (note added to the re-enable path).
- [ ] **I-12a: fsync raw-store writes** (`rawstore.go:49-58`) and sweep orphaned `.tmp` files on startup.
- [ ] **I-12b: strict seeds.yaml parsing.** Enable `yaml.Decoder.KnownFields(true)` in `config.go` so misspelled keys fail loudly instead of silently defaulting.

## Exit criteria

`go test ./...` green including the new regression tests; each wave's audit-trail entry names the findings it closes (I-N ids). When every box is ticked, delete this file.
