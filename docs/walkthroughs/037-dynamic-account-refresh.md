# 037 — Dynamic Account Refresh from UCSD Libguide

## Context

Walkthrough 036 shipped the author-tier classifier keyed on `data/known_political_x_accounts.yaml`. The YAML's `accounts.congress.{house,senate}` was a one-time hand-compiled snapshot — fine at the time, but it silently goes stale as members resign, flip parties, or change X handles. This walkthrough adds a scripted refresher so the congressional sections stay current.

**Source chosen:** UCSD Library's *Congressional Twitter* libguide. It covers both chambers (`/senators`, `/reps`), is publicly accessible, renders a plain `<table>` per alphabetical tab, and doesn't require authentication. Tried pressgallery.house.gov first — it blocks automated fetches (403). UCSD libguide is the reliable primary.

**Known tradeoff:** UCSD only exposes state abbreviations (`NC`), not house districts (`NC12`). Your current YAML has district-level data from the original pressgallery-sourced pasted-text import. The merge preserves existing district codes when the handle still matches; when a rep's handle changes (new member, handle rebrand), the district data drops back to state-only until the YAML is manually enriched.

## Changes

### ETL module

- `analysis/src/etl/refresh_accounts.py` — new standalone module. Responsibilities:
  - `fetch_html(url)` — `requests`-based fetch with a real browser User-Agent. Some gov/edu sites 403 on default requests UA; UCSD is lenient but the UA matches what we needed for other sources.
  - `parse_members_html(html, chamber)` — BeautifulSoup walk over every `<table>` on the page. UCSD's A/B/C/… tabs are rendered as separate tables all present in the DOM, tab toggling is pure CSS, so one pass over `<table>` elements hits everything.
  - Rows are filtered by anchor URL: first cell must contain an `<a href>` matching `x.com/<handle>` or `twitter.com/<handle>`. `_extract_handle()` normalizes both into a bare `handle` and a canonical `https://x.com/<handle>` URL. Non-member rows (layout tables, header rows, rows without X links) are dropped defensively.
  - Deduping is case-insensitive on handle.
  - `merge_members_into_yaml(existing, scraped, chamber)` — pure function (no I/O). Replaces `accounts.congress.<chamber>` wholesale, preserves `accounts.executive_branch` + top-level `affiliated` + `sources` + every other key. Returns `(new_payload, diff)`. Diff counts adds / removes / changes per chamber, plus how many house-district codes were preserved.
  - `write_atomic_yaml(path, payload)` — writes to `<path>.tmp` then `os.replace`s. A crash mid-write cannot corrupt the YAML.
  - `refresh_accounts(path, dry_run)` — orchestrator. Fetches both pages, merges, writes (unless `--dry-run`), returns a `RefreshSummary`.
  - `main()` — argparse CLI with `--yaml` (default `data/known_political_x_accounts.yaml`) and `--dry-run`. Prints a human-readable diff summary capped at 20 lines per add/remove/change bucket.

### Merge rules (worth noting)

Three "prefer existing when present" rules reduce churn on every refresh:

1. **District codes.** If the existing entry's `state_or_district` matches `^[A-Z]{2}\d+$` (like `NC12`) and the state prefix matches the scrape, keep it. UCSD gives state-only; we don't clobber richer data.
2. **Handle casing.** X handles are case-insensitive; UCSD renders them lowercase while the existing YAML uses camelCase (`@RepTomBarrett`). When handles match case-insensitively, we preserve the existing casing. Without this, a one-time switch from pressgallery to UCSD would show ~90 spurious "handle changed" entries in the diff.
3. **URL casing.** `url` tracks whatever handle casing we kept.

Name changes are *not* preserved — if UCSD says "Earl Carter" and the existing YAML says "Buddy Carter", the refresh writes "Earl Carter". That's an honest reflection of UCSD's canonical naming, and the dry-run surfaces every name change for operator review before writing.

### Orchestration

- `run.ps1` — new `refresh-accounts` subcommand + `[switch]$DryRun` parameter. Separate from `analyze`: you don't want to hit two .gov sites every pipeline run, and the refresh is an intentional operator action with a diff to review.
- `analysis/requirements.txt` — adds `beautifulsoup4>=4.12`.

### Tests

- `analysis/tests/test_refresh_accounts.py` — 19 offline tests (no network):
  - `TestHelpers` (5): `_normalize_name` for "Last, First" and plain formats; `_extract_handle` for x.com, twitter.com, and non-X URLs.
  - `TestParseMembersHtml` (6): fixture HTML with multiple tables, rows-without-X-links, chamber param validation, first-name-first display, state/party extraction.
  - `TestMergeMembersIntoYaml` (6): district preservation when state matches, member drop when not in scrape, new-member add, executive_branch + affiliated untouched, counts update, party-flip surfaces in diff.
  - `TestAtomicWrite` (2): roundtrip preserves payload; tmp file is cleaned up.

## Verification

- All 19 new tests pass. Full affected-module run (`test_refresh_accounts` + `test_account_classifier` + `test_propagation` + `test_rich_aggregators` + `test_review`) passes.
- Live dry-run against UCSD: fetched **99 senators + 437 representatives** (House has 435 voting members; the 437 includes 2 non-voting delegates who still have X accounts). Merged diff against the current YAML: Senate +0/-1/~1, House +16/-15/~39, **420 house district codes preserved**.

## How to run

```powershell
.\run.ps1 refresh-accounts -DryRun
# Review the ±/~ diff
.\run.ps1 refresh-accounts
# YAML is rewritten atomically. Commit if it looks right.
.\run.ps1 analyze -Tasks accounts,snapshots
# Re-seed account_profiles from the refreshed YAML and regenerate caches.
```

Cadence: monthly is probably the sweet spot — captures new-member swearings-in after elections plus mid-term resignations without being noisy.

## What is deliberately out of scope

- **pressgallery.house.gov as a primary source.** It 403s automated fetches; the effort to sidestep that (Selenium, rotating proxies) is not worth it when UCSD covers the same data without the fight. If you want to keep pressgallery-derived district numbers current, you can continue hand-editing the YAML between automated refreshes — the merge will preserve your edits.
- **Automatic scheduling.** `setup-scheduled-task.ps1` doesn't run the refresh. Intentional — this is an operator action that produces a diff worth reviewing, not a silent overnight mutation.
- **Refreshing `executive_branch`.** That section is hand-verified per administration; no public feed matches its shape.
- **Refreshing `affiliated`.** PACs / think tanks / party committees aren't enumerated by any single source we want to depend on — stays hand-curated.

## Roadmap update (walkthrough numbers shift by one)

This walkthrough was inserted before the original 037 (inference_method column). Subsequent work renumbers:

| # | Scope |
|---|---|
| 037 | (this) Dynamic account refresh from UCSD libguide |
| 038 | `inference_method` column + dead heuristic-kwargs cleanup + frontier state CHECK |
| 039 | Embedding-mode narrative clustering default + aggregator confidence pre-filtering |
| 040 | Cache + versioning + stubs cleanup |
| 041 | Propaganda pipeline — backend |
| 042 | Propaganda pipeline — surfaces |
| 043 | Calibration harness (after golden set exists) |
