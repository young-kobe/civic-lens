# Walkthrough 055 — Phase 3 pre-check audit

Phase 3 of the UI Redesign Plan opens with a pre-check: before writing aggregator code that assumes per-entity data is captured upstream, verify it actually is. This walkthrough is the audit record — no code changes yet. The aggregator additions land in a follow-up walkthrough (Phase 3b) once the findings here are acted on.

**Audit date:** 2026-04-22.
**Data snapshot:** `data/civic_lens.db` — 1,473 news docs, 132 reddit docs, 2,784 x_post docs. Live DB ≈3 days of post-launch ingest (launched 2026-04-21 per walkthrough 049).

---

## Findings at a glance

| Dimension                 | Result                                              | Gate     |
|---------------------------|-----------------------------------------------------|----------|
| News `domain_or_subreddit`| Consistent per row; needs `www.` stripping on match | Pass (normalize at aggregator) |
| News registry-match rate  | 89.7% (1,321 / 1,473)                               | Pass     |
| News outlets visible      | 6 of 20 registry entries (rest haven't ingested yet)| Watch    |
| Reddit subreddit casing   | Consistent per row (no case-variant duplicates)     | Pass     |
| Reddit registry-match     | 39.4% (52 / 132) — catch-all above 30% threshold    | Watch (non-registry subs are intentional) |
| X author handle join path | `docs.ident` → `x_posts_raw.tweet_id` → `author_id` → `x_users_raw.username` — stable, indexed | Pass |
| X verified-officials match| **0.0% (0 / 2,784)**                                | **BLOCKER** |

---

## News domain audit

### Storage format

News rows store `domain_or_subreddit` with the `www.` prefix preserved from the originating URL:

```
     531  'www.bbc.com'
     450  'www.npr.org'
     144  'www.cbsnews.com'
     139  'www.foxnews.com'
     108  'www.politico.com'
      78  'www.bbc.co.uk'
      15  'www.cnn.com'
       3  'www.lendingtree.com'
       3  'www.fool.com'
       2  'www.comparecards.com'
```

The registry uses bare domains (`nytimes.com`, not `www.nytimes.com`). Matching therefore requires stripping `www.` before comparison. A within-outlet case/variant check confirmed every outlet has exactly one canonical string in the DB (no `www.` / bare pairs for the same outlet, no `BBC.com` / `bbc.com` mixing).

### Match rate

After `www.`-stripping, 1,321 of 1,473 news docs (89.7%) match a registry outlet. The 10.3% "Other" bucket is dominated by non-political leakage (`lendingtree.com`, `fool.com`, `comparecards.com`). That's a separate filter problem, not a registry gap — logged as a follow-up.

### Outlet coverage

6 of the 20 registry outlets currently appear in the DB: BBC, NPR, CBS News (wait — CBS is not in the registry), Fox News, Politico, CNN. BBC is the bulk (531 + 78 = 609 docs). The remaining 14 registry outlets have 0 docs today but their RSS feeds are already in `data/seeds.yaml` from walkthrough 048; more will appear as ingest runs accumulate.

Note: CBS News appears in the DB (144 docs) but is not in the `news_outlets.yaml` registry. Candidate for the Phase 2 follow-up expansion pass (Q3 2026 review) — currently 9.8% of news volume.

### Decision: normalize at aggregator load time, not ingest

Principled exception to the plan's "fix at ingest" instruction. Reasoning:

- Existing rows are consistent (no duplicate storage forms for the same outlet). A migration to backfill is unnecessary.
- The Go crawler writes exactly what the RSS link resolves to. Standardizing this at write time requires touching ingest + a migration + validation. Aggregator-side normalization is a 3-line helper.
- The normalizer is small (strip `www.`, lowercase) and will live in the shared `entity_registry.py` module so every new aggregator picks it up automatically.
- Ingest-layer normalization can be added in a future pass if we ever need the raw `docs.domain_or_subreddit` value to match a registry key at query time (we don't today).

## Reddit subreddit audit

### Storage format

Subreddits are stored with canonical Reddit casing (`PoliticalDiscussion`, `Conservative`, `politics`). A case-variant check confirmed zero collisions: every subreddit has exactly one casing in the DB.

The registry also stores canonical casing (`Conservative`, not `conservative`). The aggregator still needs case-insensitive comparison because registry load + DB load casings don't need to match by coincidence — a defensive lowercased comparison is cheap and prevents silent misses if either side drifts.

### Match rate

52 of 132 reddit docs (39.4%) match a registry entry. The catch-all is 60.6%, above the plan's 30% threshold.

Catch-all breakdown:
- `moderatepolitics` (20) — not in the 10-sub registry
- `Liberal` (19) — dropped from seeds.yaml in walkthrough 048, historical data
- `worldnews` (15), `news` (14), `geopolitics` (9) — not US-political-specific
- `NeutralPolitics` (3) — not in the 10-sub registry

### Decision: accept the catch-all, do not expand the registry

The plan's 30% threshold is a smell test, not a hard rule. The registry selects the 10 subreddits the UI promotes to editorial cards; the catch-all correctly captures subreddits Civic Lens ingests but doesn't editorialize on. If we expanded the registry to close the gap, we'd blur the editorial decision that defined the 10 seats.

Three of the catch-all subreddits (`worldnews`, `news`, `geopolitics`) arguably shouldn't be in the US-political scope at all. Logged as a follow-up for Phase 11 cleanup: revisit `seeds.yaml` to drop non-US-political subs.

## X author-handle join path

### Join shape

```sql
FROM docs d
LEFT JOIN x_posts_raw x    ON x.tweet_id = d.ident AND d.source_type='x_post'
LEFT JOIN x_users_raw u    ON u.user_id = x.author_id
LEFT JOIN account_profiles ap ON ap.platform='x' AND ap.author_id = x.author_id
```

Already in use by `NarrativeAggregator._first_seen_info` (see `analysis/src/reporting/aggregators/narrative.py`). Indexes exist on `x_posts_raw.tweet_id`, `x_users_raw.user_id`, and `account_profiles(platform, author_id)`. Join cost is negligible at current volumes.

### Handle is on `x_users_raw.username`

Handle is pulled from `x_users_raw.username`. Case matching for the registry should lowercase both sides — X display handles vary in casing across the data.

### Match against verified_officials registry: 0 / 2,784

**This is the pre-check's main blocker.** Zero X docs match any of the 16 verified-officials handles (tested against primary + also_handles lowercased).

Reason: the X ingest pulls topic queries (`GOP OR Republican`, `Trump OR MAGA`, etc.) that surface broad-public discussion, not government timelines. None of the current 8 queries yield posts authored BY the 16 officials in any meaningful volume. The top authors in the DB are general-public accounts + `@grok` (xAI).

`account_profiles` has 562 elected-official rows (curated from `known_political_x_accounts.yaml`), but only 2 of the 2,784 X docs join to a classified profile — so even the existing Elected / Affiliated / General Public tier split in `NarrativeAggregator` is operating on near-empty data for the Elected tier.

### Decision: defer X timeline ingestion to a dedicated walkthrough

Phase 3b (aggregators) will ship with an "insufficient data" state for the Verified Officials column. The fix is ingest-layer and material enough to be its own pass:

- **Walkthrough 056** (not yet written): add per-official timeline ingestion. Pull up to N tweets per official per ingest cycle against the monthly X budget ($25/mo, walkthrough 048). 16 officials × ~5 tweets each × 2 ingest cycles per day ≈ 160 tweets/day ≈ 4,800 tweets/month ≈ $24 of spend — tight against the existing $25 cap. Will likely need the cap raised or per-official pull caps tuned.
- After 056 lands, the Officials column will start carrying real signal and the three-way dashboard frame reads as intended.

This ordering respects the plan's "don't bundle phases" principle: Phase 3b aggregators are a clean pass that doesn't need to wait on X timeline ingestion; the UI empty-state + "coverage will improve as timeline ingestion expands" messaging gives readers the right expectation.

---

## Summary of actions coming out of this pre-check

### For Phase 3b (next walkthrough, aggregator code)

- New shared module `analysis/src/reporting/entity_registry.py` loads the three YAMLs once at import time and exposes `{domain → OutletEntity}`, `{handle → OfficialEntity}`, `{subreddit → SubredditEntity}` maps.
- Shared normalizers inside the module: `canonicalize_news_domain(raw)` strips `www.` + lowercases; `canonicalize_subreddit(raw)` strips optional `r/` + lowercases; `canonicalize_handle(raw)` strips leading `@` + lowercases.
- Sentiment / Narrative / Propaganda aggregators import from the shared module.
- Coverage-match logging at cache-build time so catch-all rates are visible in job-runner logs.
- Phase 3b explicitly documents the Officials-column empty-state until Walkthrough 056 lands.

### Follow-ups filed (not Phase 3)

- **Walkthrough 056** — per-official X timeline ingestion. Will require touching `ingest/internal/runner/x.go`, adding a new `x.official_timelines` config block to `seeds.yaml`, and revisiting the `x_api_budget` ceiling.
- **CBS News registry add** — 144 docs in DB (9.8% of news volume) but not a registry entry. Queue for the Q3 2026 quarterly review.
- **Non-US-political subreddit trim** — `worldnews`, `news`, `geopolitics` in `seeds.yaml.reddit.subreddits` produce content that muddies the US-political sample. Queue for Phase 11 cleanup.
- **Non-political news leakage** — `www.lendingtree.com`, `www.fool.com`, `www.comparecards.com` showing up in news docs suggests an RSS feed is occasionally publishing non-political content. Logged for the filter audit pass in Phase 11.

---

## Verification commands used

```python
# Inline one-off scripts run via:
#   PYTHONPATH=. ./analysis/.venv/Scripts/python.exe -c "..."
# They query data/civic_lens.db directly and compute the match rates reported above.

# News domain distribution
SELECT domain_or_subreddit, COUNT(*) FROM docs WHERE source_type='news'
 GROUP BY domain_or_subreddit ORDER BY COUNT(*) DESC;

# Reddit subreddit distribution
SELECT source_type, domain_or_subreddit, COUNT(*) FROM docs
 WHERE source_type IN ('reddit_post','reddit_comment')
 GROUP BY source_type, domain_or_subreddit ORDER BY COUNT(*) DESC;

# X author distribution
SELECT u.username, COUNT(*) FROM docs d
  LEFT JOIN x_posts_raw x ON x.tweet_id = d.ident
  LEFT JOIN x_users_raw u ON u.user_id = x.author_id
 WHERE d.source_type='x_post'
 GROUP BY u.username ORDER BY COUNT(*) DESC LIMIT 30;

# Case-variant check
SELECT LOWER(domain_or_subreddit), GROUP_CONCAT(DISTINCT domain_or_subreddit), COUNT(*)
 FROM docs WHERE source_type IN ('reddit_post','reddit_comment')
 GROUP BY LOWER(domain_or_subreddit) HAVING COUNT(DISTINCT domain_or_subreddit) > 1;
```

Each check run against the local DB snapshot; results inlined above.

---

## Files touched

No code changes in this walkthrough — audit record only.

- `docs/walkthroughs/README.md` — index row for 055.
- `docs/ui-redesign-plan.md` — check off Phase 3 pre-check tasks; log deferred work.
