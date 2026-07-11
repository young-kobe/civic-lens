# 2026-07-11 — Deep-ink light monochrome retheme

The UI moved from the warm-paper multi-hue editorial register to a
deep-ink light monochrome (Bloomberg.com/FT data-page register): crisp
cool near-white surfaces, near-black inks, one ink-blue ramp for
magnitude/identity/positive, a single reserved brick red for
negative/alert. Because all color routes through `:root` tokens +
`theme.ts` (zero hex in TSX), the retheme is a token redesign, not a
component rewrite.

## What shipped (`ui/src/index.css`)

- **Surfaces**: `--bg-app` #f7f5f1 (warm paper) -> #fbfbfc; panel/inset
  warm creams -> cool greys. Paper-grain overlays deleted (body noise,
  card `::after` grain, bar/chip grain, and the child z-index scaffolding
  they required); the hero gradient's hardcoded stops replaced with
  surface tokens. Shadows reduced to minimal whispers — hairline borders
  carry structure.
- **Inks**: neutrals 400-900 deepened (`--neutral-900` #0d0e12,
  `--neutral-500` #4f5560) for contrast on the crisp surface.
- **Accent**: #23508a -> #1a3a6b (+hover/muted/light/gradient).
- **Diverging system**: the forest-green/brick sentiment pair is now
  ink-blue/brick everywhere — `--semantic-positive*`, `--chart-positive*`,
  the five-way tone ramp, the favorability trio, stance palette. Ochre
  warning survives as the one warm neutral (also the "public" tier).
  Lean (indigo/plum) and source-type tokens deepened, kept distinct.
- **Type floor**: reading text at 10px raised to 11px (chart ticks,
  post-card attributions, tooltips, control labels, table captions,
  Data Desk labels); deliberate micro-eyebrows (9-10px uppercase
  letterspaced labels) keep their designed scale.

## Palette validation (dataviz validate_palette.js, surface #fbfbfc)

- Diverging pair (#1a3a6b vs #8f2314, mid #838a97): CVD worst dE 46.4;
  all >= 3:1 contrast (the neutral midpoint was darkened from #9aa0ab,
  which sat at 2.54:1, to #838a97 to pass).
- Tone 5-ramp (#0f2a52 #5f7cab #838a97 #b05a45 #701c0f): worst adjacent
  CVD dE 20.1; all >= 3:1.
- Source trio (#142f45 #a34a1f #101013): worst adjacent CVD dE 50.1.
- The validator's chroma-floor/lightness-band checks are waived BY
  DESIGN: the scheme is deliberately monochrome, and identity is never
  color-alone in this UI (every multi-series surface carries a legend or
  direct labels).

## Why

- The owner asked for a more professional, monochromatic, higher-contrast
  identity. Deep ink + one alarm color also tightens semantics: red now
  only ever means negative.

## Follow-ups

- Manual visual pass on Windows (`.\run.ps1 dev`) across all six tabs +
  modals; screenshots were not taken in this environment.
