# 2026-04-25 — Narratives page no longer contradicts itself

The `/narratives` page used to show "Tracked: 20 narratives" in the global ticker while the Top Political Narratives card right below said "No stories are being repeated across more than one group yet." Both numbers were correct in isolation but read as a contradiction. The ticker now describes what it is — the top-N stories surfaced in the selected window — and the empty card now points readers at the per-group breakdown above instead of implying nothing is happening.

## What shipped

`ui/src/pages/Narratives.tsx`:

- `buildNarrativeTickerItems`: first ticker pill is now **"Top stories"** with hint **"in window"** (was "Tracked / narratives"). The value is unchanged — it's still `data.length`, which equals the API response size, capped by the UI's `?limit=20`. The relabel makes the windowed-top-N nature explicit so the number doesn't read as a system-wide count.
- `ClaimsSpreadingPanel` empty-state: subtitle rewritten to *"No story has surfaced in more than one group yet in this window — see the per-group breakdown above for what each is talking about."* Body explains the trigger condition: a story appears here once at least two of {news, officials, public} are repeating the same recurring claim.

## Why

`Tracked: 20` was misleading. The /narratives endpoint serves the top-N (defaulted by the UI to `?limit=20` against a 100-row cache); it is not a count of every narrative the system tracks. A reader saw the ticker and inferred we were tracking 20 distinct things; then the empty cross-tier card read as "but none of them are real," which is not what the data was saying. The data was saying: 20 (or up-to-20) per-tier narratives exist in this window, but no single one has crossed group boundaries — which is consistent with the three-way grid above already showing those 20 broken out by their first-seen tier.

The cross-tier flag itself (`aggregators/narrative.py::_is_cross_tier`) is already as loose as it can be — `True` when supporting docs span any 2-of-3 of `{news, officials, public}`. The lexical clusterer (`engine/narrative_clusterer.py`, Jaccard threshold `0.3`) often produces per-tier islands because news / Reddit / X phrase the same proposition with different vocabulary. That is an upstream classifier-quality concern, not a UI bug; tracked in `docs/todos/cross-tier-narrative-clustering.md`.

## Follow-ups

- `docs/todos/cross-tier-narrative-clustering.md` — investigate why so few narratives currently cross tiers; candidates are clusterer similarity mode (jaccard vs embedding), claim-extraction prompt drift across source types, and tier-skewed volume.
