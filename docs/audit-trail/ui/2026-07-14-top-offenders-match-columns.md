# 2026-07-14 — Make "Top flagged offenders" agree with the tier columns

The "Top flagged offenders" card on the Propaganda page (`ui/src/pages/Propaganda.tsx`, `topFlaggedOffenders` / `toOffenderRow`) now ranks the same population its own per-tier columns show. Previously it excluded any source with fewer than 10 scored posts, so it could headline The Federalist (22.2%) and The Intercept (19.4%) while the "Politicians & officials" column directly beneath it — which has no volume floor — showed JD Vance (37.5%, 8 scored), Kash Patel (28.6%, 7 scored), and Mike Johnson (22.2%, 9 scored) ranked higher by rate. The card contradicted the data under it. The floor is now on flagged-post count, not scored-post volume, so genuine small-sample offenders rank by their true rate and carry a visible "low sample" caveat instead of being silently dropped.

## What shipped

- Hard gate changed from `total_docs >= 10` (`MIN_OFFENDER_VOLUME`) to `flagged_docs >= 2` (`MIN_OFFENDER_FLAGGED`). This still keeps a single flagged post from reading as a 100% rate at the top of the board — the documented reason the old floor existed — without hiding sub-10-scored sources that the tier columns already surface.
- `LOW_SAMPLE_OFFENDER_DOCS = 10`: rows below this scored-post count still rank but get an inline "low sample" marker in the row's why-line (`.ranked-entity-lowsample`, `--semantic-warning`), matching the low-sample-caveat pattern in `ByPartySection` and the `low_sample` glossary term.
- Method popover and empty-state copy reworded to describe the flagged-post gate and the low-sample marker instead of the old volume exclusion.
- Sort and tiebreak unchanged (rate desc, then flagged-post count desc), so a rate tie still ranks the higher-volume source first.

## Why

- Two ranking surfaces on the same page disagreed: the summary card applied a 10-scored-post floor that the `RankedEntityList` tier columns (fed straight from the aggregator, which applies no floor) do not. A reader saw JD Vance at 37.5% in the officials column while the "highest flagged-rate sources" card topped out at 22.2% — the card read as broken.
- Resolution chosen (over hiding sub-10 sources everywhere or annotating the gap): show the true top-by-rate and caveat the thin samples, per the labeling-discipline rules in `.agent/rules/media-analysis.md` — surface the number honestly with its sample context rather than suppress it.

## Follow-ups

- None. Backend aggregator (`analysis/src/reporting/aggregators/propaganda.py`) is unchanged; this is a presentation fix.
