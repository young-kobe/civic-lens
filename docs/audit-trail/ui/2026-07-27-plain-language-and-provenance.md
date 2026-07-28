# Plain-language sweep, front-page explainer, and received-tone provenance UI

**Date:** 2026-07-27
**Layer:** ui
**Todo:** docs/todos/provenance-and-plain-language.md (retained -- owner eyeball/commit box still open)
**Cross-link:** docs/audit-trail/api/2026-07-27-received-provenance.md

Three things landed together: a layman explanation of the analysis layer on
the front page, a glossary-driven replacement of load-bearing hover-only
jargon across the app, and a UI surface for the new received-tone
provenance fields (docs/audit-trail/api/2026-07-27-received-provenance.md).

## What shipped

### Front-page explainer

- `ui/src/pages/Home.tsx` section 5 ("How it works") expands its "Score"
  step into four plain-language judgments the model makes per post (tone;
  persuasion techniques; automation signals; repeated claims grouped into
  a "story"), each stated without jargon, followed by a line naming that
  every judgment carries a confidence score, a sample is human-reviewed,
  and low-n numbers are withheld rather than guessed. A closing "What this
  isn't" paragraph restates sample-not-poll / flag-not-verdict for a
  reader who only reads this one section.

### Glossary + DefinitionChip as the tooltip standard

- `ui/src/services/glossary.ts` is the single source of reader-facing term
  definitions -- every `DefinitionChip` and every plain-language bucket
  function (`saturationLevel`, `coordinationLevel`) reads from `GLOSSARY`
  so the same term reads identically everywhere it appears. Nine entries
  added this pass: `engagement_weighted`, `divergence`, `posting_cadence`,
  `claim_match_confidence`, `window`, `template_proxy`, `story`,
  `speaker_tier`, plus the existing set. `DefinitionChip` is now the
  touch-accessible standard for load-bearing definitions that used to live
  only in a hover `title=` attribute (invisible on touch); `MethodPopover`
  remains for longer methodology explanations, `CollapsibleInfo` for
  expandable detail blocks.
- Dead shared `ui/src/components/common/ConfidenceBadge.tsx` deleted (zero
  callers -- `BotActivityProfiler.tsx` has its own local one that was
  already shadowing it).
- Jargon fixes across `Narratives.tsx`, `Propaganda.tsx`,
  `BotActivityProfiler.tsx`, `DataDesk.tsx`, `pages/home/DigestSection.tsx`,
  and shared components: `template_score` -> "templated-language score"
  (glossary `template_proxy`, explicitly flagged as a proxy, not a
  copy-paste-similarity measure); "member docs" -> "posts in this story";
  "Analyzed docs" -> "Posts analyzed"; "Top flagged offenders" -> "Highest
  flagged rates" (no moral-judgment naming, media-analysis rule 1);
  "Posts scanned"/"scored" unified to "scored"; `n=N` -> "N posts"
  wherever it rendered as reader-facing copy (`LeanLabel.tsx`'s evidence
  line, `PostCard.tsx`'s stance chip now reads "supportive"/"critical"/
  "neutral"/"mixed" instead of the raw `sentiment_label` enum value,
  `PublicSentiment.tsx`'s per-row tone bars and same/cross-party alignment
  line, `EntityProfileCard.tsx`'s received-tone stat label); raw `taskType`
  enum values humanized via `DataDesk.tsx`'s `TASK_LABEL` map ("account_tier"
  -> "Account tier", "targets" -> "Target stance", etc.); `RangeCaption.tsx`'s
  multi-model caveat reworded ("scored by N different model versions...
  numbers may not be directly comparable"); `DataDesk.tsx`'s "Cross-signal
  matrix"/"Small multiples" (now "Trend snapshots") given plain subtitles;
  `ThreeWayGrid.tsx`'s toolbar gained a one-line hint spelling out what
  Left/Right/Center mean in terms of outlet lean and officials' party.
- `GlobalTicker.tsx`'s `TickerItem.label` widened from `string` to
  `ReactNode` so a ticker label can embed a `DefinitionChip` in place --
  plain strings still work unchanged everywhere else.

### Received-tone provenance UI

- `ui/src/types.ts` mirrors the new API contract: `ReceivedSourceCell`
  and `ReceivedTone.receivedFromGroups`/`.receivedFromTop`.
- `ui/src/services/provenanceLabels.ts` (new): `sourceGroupLabel()` builds
  the display label for one provenance group from its raw `sourceClass` +
  `lean` (e.g. "left-leaning outlets", "Republican officials", "sampled X
  users") -- the backend emits only raw enum values, this is the one place
  the label is built, shared by every caller so the same group never reads
  two different ways. `topGroupsByShare()` returns the top-N cells by
  share, used by both surfaces below.
- `PublicSentiment.tsx`'s `EntitySentimentModal` gains a "Where this tone
  comes from" block (`ReceivedProvenanceBlock`) when `received.volume > 0`:
  a plain-language lead sentence naming the top 1-2 provenance groups and
  their share, a full group-share bar list, and up to 8 named top sources
  (`EntityAvatar` + lean/party chip for registry sources, `@handle` for
  sampled X authors). Renders nothing when both `receivedFromGroups` and
  `receivedFromTop` are empty -- never a guessed breakdown.
- `EntityProfileCard.tsx`'s `officialToneStats()` gains a "Mostly from
  {group1}, {group2}" line built from the same `topGroupsByShare()` +
  `sourceGroupLabel()` helpers, so the card and the modal describe the
  same group identically. **This now renders visibly**, not just on
  hover: `EntityStat` gained an optional `hint` field, rendered as a
  displayed line under the stats row (mirroring the existing "Reads as:"
  line's placement and styling, new `.entity-card-stat-hint` CSS class) --
  the hover tooltip (`title`) still carries the same text as a redundant
  accessible fallback. Renders nothing when `receivedFromGroups` is empty.
- Other `PublicSentiment.tsx` jargon fixes: the engagement-weighted-net
  log-formula hover replaced with the `engagement_weighted` glossary entry
  via `DefinitionChip`; "Divergence from the public" card subtitle gained
  a `divergence` `DefinitionChip`; "Dem officials"/"GOP officials" spelled
  out to "Democratic officials"/"Republican officials"; every `pts`
  literal (`ToneDivergenceCard`'s row tooltip, `MoversTicker.tsx`'s delta
  pill) routed through `formatPts()`, which renders "points".

## Why

The owner's three asks (provenance, plain language, an explainer) share
one root cause: the app's numbers were legible to someone who already
knew the data model, not to the reader they're actually served to. The
glossary-as-single-source pattern and the "hint" field on `EntityStat` are
both instances of the same fix -- move the plain-language reading out of
a hover-only aside and onto the page itself, without duplicating the
wording per call site.

## Follow-ups

- `GlobalTicker.TickerItem.label`'s widened `ReactNode` type is not yet
  used anywhere beyond what shipped -- the Propaganda/DataDesk Saturation
  ticker label is a candidate for a `DefinitionChip` if wanted.

## Addendum (2026-07-27): party-collective panel

`targetTone.collectives` (gop_collective/dem_collective) now has a UI
surface: `PartyTonePanel` (`ui/src/pages/publicSentiment/PartyTonePanel.tsx`),
placed on `PublicSentiment.tsx` below the tone-trend/source-signals row and
above the three-way grid. Two side-by-side cards (stacking on mobile via
the existing `.grid-2` rule) -- "Democratic Party" and "Republican Party"
-- each render the collective's net tone (or "too few to score reliably"
below the sample floor), volume, an engagement-weighted figure behind the
existing `engagement_weighted` `DefinitionChip`, a speaker-tier and
top-topic breakdown, and "Where this tone comes from" provenance. Sample
posts, when present, sit behind a "Show sample posts" toggle using
`PostCardList` with a `sampleNote`. The panel always renders, with an
honest empty-state line when neither collective has volume in the window
-- never a hidden panel. `ToneBarRows` and `ReceivedProvenanceBlock` moved
out of `PublicSentiment.tsx` into a new `ReceivedToneBlocks.tsx` module so
both the entity modal and this panel import the same code instead of the
two page modules importing each other.
