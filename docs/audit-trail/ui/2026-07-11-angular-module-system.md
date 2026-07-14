# 2026-07-11 — Angular hairline module system + serif removal + tone-color standardization

The dashboard adopts a Bloomberg "contained angular box" register. Every module is
now a square-cornered, shadowless box delineated by a single 1px hairline border, and
the grids tile them with tight consistent gaps while still hugging content (ragged
bottoms are intentional — a short module stays short, next to a crisply-bounded tall
one). Serif fonts are gone. The tone-intensity distribution bar was pulled onto the
site's own semantic tone palette so it stops clashing with the tier rows beside it.

## What shipped

- **Square modules** (`ui/src/index.css`): `--radius-md/lg/xl` set to `0` (they were used
  only by module surfaces — cards, modals, post/tab/digest panels); `--radius-sm` (buttons/
  inputs) and `--radius-full` (pills) unchanged. Explicit `border-radius: 0` on the
  `--radius-sm` module surfaces (`.top-metrics`, `.reads-as-today`, `.how-this-works`,
  `.entity-card`, `.card-note`, `.bot-section-label`).
- **No module shadows**: removed the two-layer `box-shadow` from `.card`, `.top-metrics`,
  `.how-this-works`, and `.entity-card` (incl. its hover lift/`translateY` — hover now reads
  via border + background). Structure is carried by the hairline border alone.
- **Tighter tiling**: grid `gap` `--space-3` → `--space-2` on `.dashboard-grid`, `.grid-2/3`,
  `.grid-auto`, `.three-way-grid`, `.two-way-grid`; all keep `align-items: start`. Refreshed the
  stale `.dashboard-grid` comments that still described the old equal-height/stretch behavior.
- **Serifs removed**: `--font-display` and `--font-lead` repointed from the Source Serif stacks
  to the Inter/IBM Plex sans stack (token names kept, so the ~15 selectors referencing them
  de-serify with no per-selector edits).
- **Tone palette standardized**: the `--tone-*` 5-step diverging ramp is redefined from the
  vivid semantic pair — `--tone-strong-neg = --semantic-negative` (#d0261a), `--tone-mild-neg`
  #e07a63, `--tone-neu-solid` #9aa0ab (gray midpoint), `--tone-mild-pos` #6b8fd4,
  `--tone-strong-pos = --semantic-positive` (#1a5fd0). The `.mini-bar-*` intensity classes were
  repointed off the muted `--chart-*` ramp onto these tokens, so the intensity bar matches the
  tier-row tone colors. Validated with the dataviz palette validator (worst adjacent CVD dE 24.9;
  gray midpoint intentional for a diverging bar; segment % labels relieve light-tint contrast).

## Dead code removed (as part of the color work)
- `--tone-grad-*` (5 gradient tokens) and `--tone-neu-text` — no consumers.
- `--chart-positive-strong/-soft`, `--chart-negative-strong/-soft` — orphaned once the mini-bar
  stopped using them.
- theme.ts: the entire `tone*`/`toneGrad*`/`toneNeuText` `COLORS` block (unused) and the four
  orphaned `chart*Strong/Soft` keys.

## Why

- Iterative design review against Bloomberg: the user wanted "defined angular borders that keep
  everything feeling contained and clean," serifs gone ("fonts are mismatched"), and the tone
  colors standardized ("the bar colors don't match the rest of the site"). A follow-up reference
  clarified that different-height (ragged) columns look fine *as long as* each module is a crisply
  bounded box — hence content-hug retained, equal-height tiling rejected.

## Follow-ups
- Per-page reflows that remove the specific dead-space the review circled: Overall Tone
  top-metrics (tier-row band + intensity-bar size), Propaganda (remove News-vs-social, compact
  technique legend), Data Desk (2×2 module block), Bot Detector (2-up amplification cards).
- Buttons/inputs kept a 3px `--radius-sm`; square them too if a fully-angular control set is wanted.
