# 2026-07-11 — Narrative drill-down: news cards get a body dek

Supporting-doc cards in the narrative detail modal now carry a one-line body preview for **every**
doc, including news. Previously `build_supporting_docs` emitted the snippet only for title-less docs
(social posts), so a news supporting doc rendered as a bare headline — the UI leads with the headline
and shows the snippet as the dek beneath it, so a null snippet meant an empty card body.

## What shipped

- **`reporting/aggregators/narrative/projector.py`** — `build_supporting_docs` now sets
  `snippet = text_snippet(r.text)` unconditionally (was `if not r.title else None`). The batched
  supporting-docs query already selects `text_head` (first `SNIPPET_MAX_CHARS*4` chars of `d.text`)
  for all source types, so the body preview is available for news too. Social behavior is unchanged
  (no title -> the snippet is shown in place of the headline); news now gets a dek under the
  headline. The sentiment `reasoning` was already surfaced on the card — no change there.
- **`analysis/tests/test_narrative_supporting_docs.py`** (new) — intent guard: a news row with a
  headline AND body text still yields a snippet (fails if the `if not r.title` gate returns);
  title-less social rows still use the body as the headline; empty body yields a null snippet (no
  fabricated dek); reasoning is surfaced from the sentiment JSON.

## UI (no behavior change, cross-layer)

`PostCard` already renders `snippet` as the dek under a headline (the code comment: "news cards lead
with the headline, so the body is the dek"), so no component change was needed — the news cards were
render-ready and simply never received a snippet. Updated the `SupportingDoc.snippet` doc comment in
`ui/src/types.ts` to say it's also the news dek, and added news deks to the mock supporting docs in
`ui/src/services/fixtures.ts` (via a new optional `snippet` param on the `supDoc` helper) so the
enrichment is visible in fixtures mode without a snapshot rebuild.

## Verification

- `analysis` tests green (48 across evidence/narrative/new dek test); UI `typecheck` + `build` green.
- On real data the dek appears after the next `save_snapshots` run (the cache is the contract);
  fixtures show it immediately.
