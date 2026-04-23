# 2026-04-22 — Narratives cross-tier panel: unified title, 5-story cap

The cross-tier panel on `/narratives` now carries a single title — "Top political narratives" — across both empty and populated states, and the list is capped at 5 rows to match the mockups.

## What shipped

`ui/src/pages/Narratives.tsx::ClaimsSpreadingPanel`:

- Title is now **"Top political narratives"** in both states (was "Claims spreading between groups" when empty).
- Empty-state subtitle rewritten: *"No stories are being repeated across more than one group yet — the news, officials, and the public aren't overlapping in this window."* with a muted "Check back as coverage develops." body.
- Populated-state subtitle unchanged in shape but now describes the visible count after slicing: *"N stories are being repeated by more than one group — the news, officials, and the public are all talking about them."*
- New `CROSS_TIER_LIMIT = 5`; `narratives.slice(0, CROSS_TIER_LIMIT)` feeds both the subtitle count and the row list. Input is already sorted by `supporting_doc_count` desc in `aggregators/narrative.py`, so the 5 rows are the 5 most-amplified cross-tier stories.

## Why

User: *"match the mockups in verbage … and can we make sure we limit to five like the mockups."*

The previous copy leaked two bits of internal vocabulary ("Claims spreading between groups", "in more than one of The News / Officials / The Public") that didn't match the mockup's reader-facing framing. Unifying the title also eliminates the "wait, the card renamed itself" jank when a time window goes from empty to populated.

The 5-cap is a design decision, not a data one — the backend still returns every cross-tier narrative, so widening the cap later is a one-line change.

## Follow-ups

None.
