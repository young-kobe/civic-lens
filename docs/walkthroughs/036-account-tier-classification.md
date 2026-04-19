# 036 — Account Tier Classification

## Context

Walkthrough 035 split the Narratives tab into News Media and Social Media sections and committed to a follow-up: further split Social Media into *elected officials*, *politically affiliated*, and *general public* based on **who first said the claim**. This walkthrough lands that split.

Approach: **Option C (hybrid)**. A curated YAML is the source of truth for high-confidence institutional accounts (US president / VP / White House, congressional caucuses, major party committees, major PACs, major think tanks). An LLM classifier fills in individuals with meaningful post volume — journalists, pundits, strategists — that a list can't enumerate. Anyone not matched or affirmatively classified defaults to *general public*.

Reddit authors are intentionally out of scope. Elected officials and named political organizations are rare on Reddit, and we do not ingest the equivalent profile signal (description, follower graph, verified flag) the LLM needs.

## Changes

### Schema

- `data/migrations/010_account_profiles.sql` — new table:
  ```
  account_profiles(
      profile_id PK,
      platform CHECK IN ('x','reddit'),
      author_id,
      display_name,
      tier CHECK IN ('elected_official','affiliated','general_public'),
      classification_method CHECK IN ('curated_list','llm'),
      classified_at,
      confidence,       -- NULL for curated_list; 0..1 for llm
      reasoning,        -- llm path
      notes,            -- curated path
      UNIQUE(platform, author_id)
  )
  ```
  Indexed on `tier` and `(platform, author_id)`. The `general_public` tier is *allowed* in the CHECK but is only written by the LLM path when the model affirmatively picks it — this avoids re-classifying the same author on every run. Absence from the table means "default to general_public" at query time.

### Seed data

- `data/known_accounts.yaml` — minimal starter seed (~25 entries) under two keys, `elected_official:` and `affiliated:`. Each entry has `handle` and optional `notes`. Comments in the file document the intended growth path (Senate + House + governors + cabinet + party committees ~600 handles). Designed for easy human curation — the user maintains this file; nothing else in the codebase generates it.

### Engine

- `analysis/src/engine/account_classifier.py` — new `AccountClassifier`:
  - `load_curated(yaml_path)` — upsert on `(platform, author_id)`. Resolves each handle to `x_users_raw.user_id` when we've ingested a post from that account; otherwise stores the lowercased handle so the row is in place when we first see them. Curated rerun overwrites tier changes (YAML is source of truth).
  - `classify_with_llm(limit)` — LLM classifier over X authors with `>= MIN_POSTS_FOR_LLM_CLASSIFICATION` (3) posts in the last 30 days, not already in `account_profiles`. Per-author call uses display name, self-description, verified flag, followers, and up to 5 recent posts as evidence. Per-run cap `MAX_LLM_CLASSIFICATIONS_PER_RUN = 200` so a misconfiguration can't burn through a quota. `INSERT ... ON CONFLICT DO NOTHING` on the LLM path — curated always wins, and we don't re-LLM an author we already classified.

### Prompts + schema

- `analysis/src/engine/prompts.py` — adds `ACCOUNT_CLASSIFIER_PROMPT_VERSION = "account-classifier-v1"`, `ACCOUNT_CLASSIFIER_SYSTEM_PROMPT`, and `ACCOUNT_CLASSIFIER_USER_PROMPT_TEMPLATE`. System prompt explicitly defines each tier (covers the "where does X fit" judgment — strategists/PACs/think tanks = affiliated per the user's decision in the roadmap discussion), flags that verified-on-X is not enough to promote out of general_public, and requires brief reasoning. User template passes platform, handle, display name, description, verified, followers, and recent-post samples.
- `analysis/src/llm/schemas.py` — adds `ACCOUNT_CLASSIFIER_SCHEMA` (tier enum + confidence in [0,1] + optional reasoning).

### Orchestration

- `analysis/src/common/settings.py` — adds `known_accounts_yaml` (default `data/known_accounts.yaml`) and `account_classifier_llm_enabled` (default `true`). The LLM branch only runs when the global `llm_enabled` **and** this per-task flag are both true.
- `analysis/src/scheduler/job_runner.py` — new step `run_account_classification()` between narrative clustering and snapshots:
  1. `load_curated(yaml_path)` — always runs.
  2. `classify_with_llm()` — runs only if LLM is available.
  Pipeline is now eight steps; old log messages renumbered `Step N/8`. New CLI task value `accounts` (`.\run.ps1 analyze -Tasks accounts` runs just this step).

### Aggregator

- `analysis/src/reporting/aggregators/narrative.py` — `_first_seen_info` now returns `(source_type, domain, tier)`. For `x_post` source types it joins `docs.ident = x_posts_raw.tweet_id` → `x_posts_raw.author_id` → `account_profiles.tier`. For news / reddit it leaves tier as `NULL`. The aggregator defaults `x_post` tier to `'general_public'` when the account_profiles row is absent, so the UI gets a clean 3-way split without null-handling on that path.
- `analysis/src/reporting/models/aggregator_models.py` — `NarrativeSummary` gains `first_seen_tier: Optional[str]`, serialized as `first_seen_tier` in `to_dict()`.

### UI

- `ui/src/types.ts` — new `AccountTier = 'elected_official' | 'affiliated' | 'general_public'` and `NarrativeSummary.first_seen_tier` with inline comment explaining scope.
- `ui/src/pages/Narratives.tsx` — Social Media Narratives is now four sub-cards instead of one:
  1. **Social · Elected Officials (X)** — x_post first-seen with tier=elected_official.
  2. **Social · Politically Affiliated (X)** — x_post with tier=affiliated.
  3. **Social · General Public (X)** — x_post with tier=general_public or unclassified.
  4. **Social · Reddit** — reddit_post + reddit_comment (no tier split; reasoned inline in the method note).
  A panel between News and the X sub-cards explains the three tiers in plain language ("elected officials (current/former officeholders and institutional accounts)", etc.) and notes the curated-list + LLM classifier source. The top-of-page disclaimer adds a sentence about the tier split: *"social X-origin narratives are further split by who first said it."*

### Tests

- `analysis/tests/test_account_classifier.py` — 7 tests:
  - Curated loader writes rows with correct tier and method.
  - Curated loader is idempotent (rerun produces 1 row, not 2).
  - Curated rerun overwrites tier change (YAML is source of truth).
  - LLM classifier persists affirmative tiers for all candidates (two canned responses: affiliated + general_public).
  - LLM classifier skips already-classified authors on a second run.
  - NarrativeAggregator returns the classified tier for x_post narratives via the join.
  - NarrativeAggregator returns `None` for news narratives (tier only applies to X).

### Dependencies

- `analysis/requirements.txt` — added `PyYAML>=6.0` (new dependency from the curated loader).

## Verification

- `./civic-ingest.exe migrate` — migration 010 applied cleanly against the dev DB.
- `pip install PyYAML` into the existing venv.
- `python -m unittest analysis.tests.test_account_classifier` — 7/7 pass.
- Full affected-module run (`test_account_classifier` + `test_propagation` + `test_rich_aggregators` + `test_review`) — 41/41 pass.
- `cd ui && npm run typecheck` — clean.

## Deploy

1. `.\run.ps1 migrate` — applies migration 010.
2. `pip install -r analysis/requirements.txt` — pulls PyYAML into the venv.
3. Edit `data/known_accounts.yaml` to expand the curated seed (Senate, House, governors, cabinet — targets ~600 handles over time).
4. Re-run `.\run.ps1 analyze` — the new `accounts` step runs between narratives and snapshots. The Narratives tab will show the 3-tier X split after the next `snapshots` step regenerates the narrative cache.

## What is deliberately out of scope

- **Reddit tier classification.** Electeds/PACs are rare on Reddit; no high-signal profile data. If this changes, add `platform='reddit'` rows via a curated Reddit handle list — the schema already supports it.
- **LLM re-classification when a user's role changes.** If @journalist becomes a press secretary, their stored tier stays stale until either (a) the curated YAML adds them or (b) we manually delete their row and re-run. Acceptable for now; re-classification triggers can land in a later walkthrough if this turns out to matter.
- **Confidence-gated narrative display.** We don't currently hide low-confidence LLM classifications from the UI. Once walkthrough 041's calibration harness exists, we can threshold on `account_profiles.confidence` and surface uncertain classifications differently.

## Mid-walkthrough extension — faction metadata + richer YAML (2026-04-19)

Shortly after the first pass landed, two issues came up: the seed YAML was too thin (users wanted full Congress + Senate + executive), and the schema didn't track *which faction* (party, chamber, office) each account belongs to. Both closed in this same walkthrough:

### Bug fix: `project_root` path

- `analysis/src/scheduler/job_runner.py` — `project_root = Path(__file__).parent.parent.parent.parent.parent` (5 parents) landed on `C:\Users\kobey\` instead of the repo root. sys.path still resolved imports because `run.ps1` sets `PYTHONPATH` independently, but `project_root / settings.known_accounts_yaml` missed the repo. Fixed to four parents. Any file-system reference to the repo root now resolves correctly.

### Schema extension — migration 011

`data/migrations/011_account_profiles_faction.sql` adds seven nullable columns to `account_profiles`:

| Column | Purpose |
|---|---|
| `full_name` | Display name independent from display_name/handle |
| `party` | `D` / `R` / `I` / `L` / `G` / NULL |
| `branch` | `executive` / `legislative` / `judicial` / `party_org` / `pac` / `think_tank` / NULL |
| `chamber` | `senate` / `house` / NULL (legislative only) |
| `state_or_district` | `NY` or `CA33` or NULL |
| `office_title` | Plain-English role — "President", "Senator", "Representative", "Secretary of Defense", etc. |
| `account_type` | Source YAML label — `official`, `official_role`, `personal`, `personal/political`, `institutional`, `previous/personal`, NULL |

Indexed on `party` and `branch`. All columns are curated-only — the LLM classifier does not populate them (it classifies tier, not faction).

### Consolidated YAML — `data/known_political_x_accounts.yaml`

A user-provided richer seed replaces the original minimal `known_accounts.yaml` (now deleted). 547 congressional entries + 10 executive-branch entries + appended 13 affiliated entries (party committees + major think tanks + major PACs) = **576 rows on first load**. Top-level structure:

```yaml
schema_version: 1
platform: x
accounts:
  executive_branch:
    - name: ...
      office: ...
      branch: Executive
      accounts:                    # multi-handle per person
        - { platform, handle, account_type }
  congress:
    house:   [ { name, handle, party, chamber, state_or_district, ... } ]
    senate:  [ { name, handle, party, chamber, state_or_district, ... } ]
affiliated:                         # legacy flat key, for non-elected orgs
  - { handle, notes }
```

### Curated-loader rewrite

`analysis/src/engine/account_classifier.py` — new `_parse_curated_yaml(payload)` function flattens either shape into a list of `CuratedEntry` records:

- Executive: one row per handle (Trump's @POTUS + @realDonaldTrump + @WhiteHouse produce three rows, all with `full_name='Donald J. Trump'`, `office_title='President of the United States'`, different `account_type`s).
- Congress: one row per person; `branch='legislative'`, `chamber` lowercased, `office_title` inferred from chamber ("Senator" / "Representative").
- Legacy flat `elected_official:` / `affiliated:` top-level lists are still honored side-by-side, so the affiliated org seeds keep working.

`load_curated` UPSERT now carries all seven new columns. `classify_with_llm` leaves faction columns NULL on the LLM path (it classifies tier only).

### Aggregator — `first_seen_author` sub-object

`NarrativeAggregator._first_seen_info` now returns `(source_type, domain, tier, author_profile)`. For x_post first-seen docs, `author_profile` is a dict: `{handle, full_name, party, branch, chamber, state_or_district, office_title, account_type}`. News/reddit first-seen docs get `author_profile=None`.

`NarrativeSummary` adds the `first_seen_author` field (serialized in `to_dict()`).

### UI — faction-aware label

`ui/src/types.ts` adds `AccountProfile` interface and `NarrativeSummary.first_seen_author`. `ui/src/pages/Narratives.tsx` adds an `authorLabel(author)` helper that produces:

- `Pres. Donald J. Trump (R)`
- `Sen. Todd Young (R, IN)`
- `Rep. Alma Adams (D, NC-12)` — house districts render with a dash
- Falls back to source_type · domain when no author profile is available

The narrative row's first-seen line (previously "first seen in x_post · x.com · N days ago") now shows the rich label when the source is classified X: "first seen via Rep. Alma Adams (D, NC-12) · N days ago".

### Tests added

`analysis/tests/test_account_classifier.py` grows from 7 → 12 tests:
- `TestParseCuratedYAMLRichFormat` (4 new): executive multi-handle, house+senate, legacy flat, mixed file.
- `TestNarrativeAggregatorTierJoin.test_aggregator_returns_author_faction_payload`: verifies the payload carries party/branch/office/account_type through for classified X authors, and handle-only for unclassified.
- Existing `test_aggregator_tier_is_null_for_news` also asserts `first_seen_author is None` for news.

All 46 tests in the affected module set (`test_account_classifier` + `test_propagation` + `test_rich_aggregators` + `test_review`) pass. UI typecheck clean.

### Smoke-load numbers

First real run on the consolidated YAML — `elected_official=563, affiliated=13, total=576` — covering the full 119th Congress plus the listed executive-branch principals plus 13 party orgs / PACs / think tanks.

## Remaining roadmap

Unchanged from 035's end:

| # | Scope |
|---|---|
| 037 | `inference_method` column + dead heuristic-kwargs cleanup + frontier state CHECK |
| 038 | Embedding-mode narrative clustering default + aggregator confidence pre-filtering |
| 039 | Cache + versioning + stubs cleanup |
| 040 | Propaganda pipeline — backend |
| 041 | Propaganda pipeline — surfaces |
| 042 | Calibration harness (after golden set exists) |
