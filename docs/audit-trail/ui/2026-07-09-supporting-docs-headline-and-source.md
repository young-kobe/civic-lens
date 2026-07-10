# 2026-07-09 — Supporting-docs table: snippet fallback + X handle source

The shared `SupportingDocsTable` (Political Narratives + Overall Tone / entity
profiles) showed `(untitled)` for social posts and, on the entity-profile
sample path, `X · @x.com` for the source.

## What shipped

- `types.ts`: `SupportingDoc` gains optional `snippet` — a text preview shown
  in the Headline column when `title` is null.
- `components/common/SupportingDocsTable.tsx`:
  - Headline cell renders `title || snippet || (untitled)`.
  - `classificationSampleToSupportingDoc` builds `snippet` from the sample's
    `full_text` (new `textSnippet`, 120-char cap) when there is no title,
    mirroring the server-side snippet the narrative path now emits.
  - The `X · @<source_name>` label is now correct because the backend sends the
    handle as `source_name` (see the analysis entry) — no adapter change.

Cross-links the analysis entry
[`analysis/2026-07-09-x-source-label-and-social-snippet.md`](../analysis/2026-07-09-x-source-label-and-social-snippet.md).

## Why

- `(untitled)` is uninformative for X/Reddit posts, which have no headline; the
  post text is the natural label.
- Source attribution must name the real account (invariant C1), not the `x.com`
  domain.
