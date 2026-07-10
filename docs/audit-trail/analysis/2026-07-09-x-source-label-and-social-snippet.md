# 2026-07-09 — X source label uses handle; social posts carry a text snippet

Two supporting-docs / classification-sample defects surfaced on the live entity
profiles at launch:

1. **`X · @x.com`** — the sentiment sampler set a sample's `source_name` to
   `domain_or_subreddit`, which for X rows is literally `x.com`. The UI renders
   `X · @<source_name>`, so entity-profile "Recent classified posts" showed
   `X · @x.com` instead of the author handle. (The narrative supporting-docs
   path was already correct — it builds the label via `_build_source_label`
   from `x_handle`.)
2. **`(untitled)`** — X and Reddit posts have no headline, so the Headline
   column fell back to "(untitled)".

## What shipped

- `reporting/aggregators/sentiment.py`: `_build_sample_dict` now sets
  `source_name` to `x_handle` for `x_post` rows; handle-less X rows (author
  missing from `x_users_raw` — the join is a LEFT JOIN) get `None` so the UI
  degrades to a bare "X" rather than "X · @x.com". News/reddit keep
  `domain_or_subreddit`. Fixes the `@x.com` label at the source; no UI change
  needed since the adapter already renders `X · @<source_name>`.
- `reporting/aggregators/narrative.py`: `_top_supporting_docs` now selects a
  480-char head of `d.text` (`substr` in SQL, so full article bodies are never
  materialized) and emits a `snippet` (new `_text_snippet` helper — whitespace-
  collapsed, 120-char cap) for rows without a title, so the narrative
  supporting-docs table shows post content instead of "(untitled)".

Cross-links the UI entry
[`ui/2026-07-09-supporting-docs-headline-and-source.md`](../ui/2026-07-09-supporting-docs-headline-and-source.md).

## Why

- Invariant C1 (source attribution): a sample that reads `@x.com` misattributes
  the post — the reader can't tell which account it came from. The handle is
  the correct, auditable source name.
- `(untitled)` is a dead-end for social posts; the post text is the natural
  headline and keeps the row informative.

## Follow-ups

- Both changes are cache-derived; they populate on the next `analyze` run
  (snapshots stage). No migration.
- The wire-shape split between `ClassificationSample` and `SupportingDoc`
  remains — see the backend aggregator audit todo.
