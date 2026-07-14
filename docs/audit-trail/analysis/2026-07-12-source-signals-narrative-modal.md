# 2026-07-12 — Source signals → narrative drill-down modal

Clicking a row in the Tone page's "Source signals, side by side" table now opens a modal that ties
that source's net-tone number to the recurring stories and posts driving it — so the reader can see
WHY a domain reads the way it does, not just the number.

## Backend (analysis)

- **`reporting/aggregators/outlet.py`** — each `OutletProfile` now carries a `samples` list: its
  highest-confidence scored posts (reasoning-gated, capped at `MAX_OUTLET_SAMPLES` via `_insert_capped`),
  each serialized to the shared `ClassificationSample` shape and **tagged with the recurring narrative**
  its doc belongs to. `_fetch_doc_narratives` builds a `doc_id -> narrative name` map (guarded — empty
  when the narrative tables don't exist); the fetch query was widened to pull the doc fields the sample
  builder needs (`_build_sample_dict`) plus the X author handle (`X_AUTHOR_JOIN_SQL`).
- **`models/aggregator_models.py`** — `OutletProfile.samples` field + `to_dict` serialization.
- Verified with an end-to-end smoke run (6 news docs + a narrative → one outlet profile with 6
  narrative-tagged samples); existing outlet/sentiment tests stay green.

## Frontend (ui)

- **`types.ts`** — `ClassificationSample.narrative?`; `OutletProfileItem.samples?`.
- **`publicSentiment/OutletSignalsPanel.tsx`** — rows are clickable (`.outlet-signal-row`, hover +
  pointer); `OutletSamplesModal` shows the source's net tone + volume, a **"Stories driving this
  tone"** list (distinct narratives among its samples, most-frequent first), and the sample posts via
  `PostCardList` (evidence highlighted). Subtitle gains "Click a source to see the stories driving its
  tone."
- **`fixtures.ts`** — `outletSample()` helper; narrative-tagged samples seeded on the mock x.com and
  nytimes.com outlets so the modal is populated in fixtures mode.
- New `.outlet-stories*` CSS.

## Verification

- Backend outlet/sentiment tests green; UI `typecheck` + `build` green. On real data the samples +
  narratives populate after the next `save_snapshots`; fixtures show the modal now.
