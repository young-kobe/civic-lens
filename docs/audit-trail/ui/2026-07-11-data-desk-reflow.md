# 2026-07-11 — Data Desk: 2×2 module block, Movers fills the dead space

The Data Desk no longer leaves a large empty band beside the small-multiples grid. The pipeline
health tables were pulled out of their nested `grid-2` and laid out as a 2×2 block: Snapshot
freshness takes the left slot Movers used to hold, and Movers moves down into the previously-empty
area beside the small multiples.

## What shipped

- **Split `PipelineHealth`** (`DataDesk.tsx`) into standalone `SnapshotFreshnessCard` and
  `HumanReviewCard` (each returns null when empty) so each table can occupy its own dashboard-grid
  slot instead of being locked in a nested `grid-2`.
- **New layout**:
  - Row 1: `SnapshotFreshnessCard` `col-span-5` + `SmallMultiples` `col-span-7`.
  - Row 2: `HumanReviewCard` `col-span-5` + `MoversBoard` `col-span-7`.
  - Spans degrade to `col-span-12` when a partner is absent (no movers / no review / no snapshots).
- **Capped the snapshot table** (`.desk-table-scroll`, `max-height:320px; overflow-y:auto`) so its
  ~21 rows scroll internally rather than towering over the small-multiples beside them.

## Why

- Round-2 review: "move Movers board to the [empty] red circle; move Snapshot freshness to replace
  Movers board … to fix [the] whitespace issue." Builds on the angular content-hug system in
  `2026-07-11-angular-module-system.md` (ragged rows are fine; the reflow just removes the big
  circled dead space).

## Follow-ups
- Last round-2 reflow: Bot Detector (2-up amplification cards + section-label header).
