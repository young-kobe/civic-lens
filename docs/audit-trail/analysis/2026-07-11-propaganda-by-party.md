# 2026-07-11 — Propaganda by party (Phase 4)

The Propaganda page can now show which partisan **side** leaned harder on persuasion techniques: a
per-party rollup over the tracked officials, plus a party tag on each flagged example.

## What shipped (analysis)

- **`reporting/aggregators/propaganda.py`**
  - New `PartyPropaganda` model + `PARTY_LABELS` map; `PropagandaOverview.by_party` (serialized in
    `to_dict`).
  - `_rollup_by_party(by_official)` groups the per-official propaganda buckets by
    `entity_profile.party`, volume-weighting the flagged rate and mean score across each party's
    officials and counting distinct officials. Party-less officials (the verified-officials
    catch-all, unaffiliated accounts) are excluded so the rollup only reports real sides; sorted by
    flagged rate desc.
  - `PropagandaExample.party` — each example now carries the author's party when they're a tracked
    official (null otherwise). The example loop resolves the entity route once and uses it for both
    the party tag and the `examples_by_entity` key, replacing the separate `_resolve_entity_key`
    helper (removed).
- **`analysis/tests/test_propaganda_by_party.py`** (new) — pins the rollup contract: grouped by
  party, volume-weighted rate/mean, party-less officials excluded, sorted by rate desc, empty on no
  officials.

## What shipped (ui, cross-layer)

- **`ui/src/types.ts`** — `PartyPropaganda` interface; `PropagandaOverview.by_party?`;
  `PropagandaExample.party?`.
- **`ui/src/pages/Propaganda.tsx`** — `ByPartyCard`: one labeled bar per party (flagged rate, sized
  vs the max party rate), the bar colored with the partisan lean hue (a legitimate data legend — the
  bar names the party; chrome stays monochrome), value + official count + a per-row hover with the
  raw counts and mean score. Rendered full-width under the three-way grid, only when `by_party` is
  non-empty. New angular `.party-bar-*` CSS.
- **`ui/src/services/fixtures.ts`** — `by_party` mock consistent with the mock `by_official` (R:
  POTUS + Johnson, D: Schumer).

## Why

- Round-1 Phase 4: surface the propaganda ↔ partisan-side correlation the three-way frame implies but
  never states outright. Uses the party already on each official's `entity_profile`, so no new query
  or schema — just a rollup + an example tag.

## Verification

- `analysis` propaganda tests green (26 incl. new rollup test); UI `typecheck` + `build` green.
- On real data `by_party` appears after the next `save_snapshots`; fixtures show it immediately.
