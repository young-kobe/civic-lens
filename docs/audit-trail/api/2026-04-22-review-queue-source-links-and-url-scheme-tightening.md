# 2026-04-22 — Review queue source links + URL-scheme tightening

Two follow-ups from the pre-deploy security review. The admin review queue now emits a permalink for every doc it surfaces, and a loose URL-scheme check in the aggregator layer was tightened.

## What shipped

### Review queue permalinks (invariant C1 compliance)

- `analysis/src/reporting/review.py::ReviewService.get_queue()` — LEFT JOINs `x_posts_raw` + `x_users_raw` to pick up the X handle for `x_post` rows, imports `_build_doc_url` from the narrative aggregator, and adds `url` to each item's `doc` dict. Null when the ingest layer didn't capture enough metadata to synthesize a permalink.
- `ui/src/types.ts::ReviewQueueItem.doc.url` — mirrored; typed `string | null`.
- `ui/src/pages/review/ReviewItemCard.tsx` — renders `View original ↗` in the card header when url is present, `target="_blank" rel="noreferrer"`, styled via the existing `.example-row-link` class used by the Propaganda examples.

C1 (`docs/INVARIANTS.md`): "Any UI surface that shows an individual doc as evidence... MUST link back to the original source." The review queue is an evidence surface by that definition; the admin user didn't have a clickable permalink before.

### URL-scheme tightening

`ident.startswith("http")` was matching values like `httpfoobar` (missing `://`) and passing them through as anchor hrefs, which would resolve as a same-origin relative path. Not an XSS vector (no `javascript:` bypass), but sloppy. Two call sites tightened to `startswith(("http://", "https://"))`:

- `analysis/src/reporting/aggregators/narrative.py:_build_doc_url`
- `analysis/src/reporting/aggregators/sentiment.py` (the `_build_sample_dict` URL helper)

The test file `analysis/tests/test_entity_registries.py:111` is a validator asserting registry domains do NOT include a scheme — left unchanged; its semantic meaning is correct.

## Why

Pre-deploy security review (general-purpose agent, 2026-04-22) against the `21-enhance-ui` branch returned two LOW findings; both patched here. The rest of the review was clean: no CRITICAL / HIGH / MEDIUM findings across API routers, aggregators, UI components, or infra diffs.

## Technical debt noted

The X-author LEFT JOIN pair (`x_posts_raw` + `x_users_raw`) now appears in five aggregator surfaces. Tracked in `docs/todos/backend-aggregator-audit.md` §1 — future consolidation into a shared `resolve_x_handle()` helper or a `docs_with_x_author` view.

## Follow-ups

None for this change. Security-review pass concluded; PR is clean for deploy.
