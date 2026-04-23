# 2026-04-22 — Bot Detector amplification examples carry real source links

The Bot Detector's amplification modal previously showed flagged posts as plain italicized quotes — no doc_id, no permalink. `NarrativeAmplification.examplePosts` was typed `List[str]` and in production was always `[]` (fixtures-only). That violated invariant C1 (every per-doc evidence surface must outbound-link to the source).

## What shipped

- `analysis/src/reporting/models/aggregator_models.py` — new `FlaggedExample` dataclass: `doc_id`, `text`, `source_label`, `url`. `NarrativeAmplification.examplePosts` now typed `List[FlaggedExample]`. `BotActivityData.to_dict()` serializes each via `asdict`.
- `analysis/src/reporting/models/__init__.py` — `FlaggedExample` exported.
- `analysis/src/reporting/aggregators/bot.py`:
  - `_fetch_bot_detection_data` SQL widened with `LEFT JOIN x_posts_raw + x_users_raw` so x_post rows carry the author handle needed for X permalinks. Call-site comment flags this as the 6th copy of the x-author join pattern — see `docs/todos/backend-aggregator-audit.md §1`.
  - `bot_docs_data` dict extended with `source_type`, `ident`, `x_handle`, `text` (was: `{doc_id, data, domain, pub_at}` only).
  - New `_pick_examples_for_indicator(indicator, bot_docs_data)` — returns up to 3 `FlaggedExample` rows whose `data.indicators` includes the indicator. Text clamped to 220 chars. URL synthesized via the shared `_build_doc_url` helper (news → http ident, reddit → `/r/{sub}/comments/{id}`, x_post → `https://x.com/{handle}/status/{id}`). Source label via the shared `_build_source_label` helper.
  - `_narrative_amplification` now calls `_pick_examples_for_indicator` instead of `examplePosts=[]`.

## Why

Invariant C1 (`docs/INVARIANTS.md`): every UI surface that shows an individual doc as evidence must outbound-link to the original. The Bot Detector's amplification modal is an evidence surface — the flagged posts were quoted verbatim as proof of coordinated amplification. Without a source link, the user has no way to verify the quote is real or read the surrounding context.

## Technical debt noted

The x-author LEFT JOIN is now duplicated in six aggregator surfaces (was five — see `docs/todos/backend-aggregator-audit.md §1` for the full list). When the consolidation sweep runs, the new call site in `_fetch_bot_detection_data` migrates alongside the others.

## Follow-ups

- UI side documented in `../ui/2026-04-22-bot-flagged-examples-with-source-links.md`.
- The open todo `docs/todos/bot-propaganda-entity-signals.md` proposes regrouping amplification cards by registered entity (outlet/official/subreddit) rather than by behavioral indicator. The new `FlaggedExample` is already entity-aware (`source_label`) so it fits either grouping when that work lands.
