# 2026-07-10 — Officials cards: received vs. expressed tone

Officials cards on the Overall Tone page now lead with **Received tone**
(how sampled posts talk about the person — the reputational signal) and
keep the old number as **Expressed tone** (the tone of their own posts),
explicitly labeled with tooltips defining each. Previously both concepts
were collapsed into a single stat labeled "Net tone", which read as
reputation but measured rhetoric.

## What shipped

- `ui/src/types.ts` — `ReceivedTone`, `TargetTopicCell`,
  `ExpressedAlignment`, `TargetToneMeta`; `EntitySentimentItem` gains
  optional `received` / `expressedAlignment`; `PublicSentimentData`
  gains optional `targetTone`. All optional so pre-target cached
  snapshots keep rendering.
- `ui/src/components/common/EntityProfileCard.tsx` —
  `officialToneStats()`: Received tone is the emphasis stat with its n
  in the label (`Received tone (n=12)`); suppressed nets (below the
  aggregator's sample floor) render as "low sample", never a number;
  Expressed tone + Posts follow. Exported via `components/common`.
- `ui/src/pages/PublicSentiment.tsx` — official cards use
  `officialToneStats`; the entity modal shows both metrics with
  definition tooltips, a "Tone toward X by topic" table (net or
  "low sample" per cell, always with n), and a one-line same-party /
  cross-party alignment note on the official's own posts framing
  cross-party negativity as the expected baseline.
- `ui/src/services/transformers.ts` — `targetTone` passthrough.

## Why

- Labeling discipline (media-analysis rules): a -66.7 shown on an
  official's card without a subject was being misread as "people are
  negative about this person" when it measured "this person posts
  negatively". Two orthogonal metrics, two explicit labels.
- Small-n honesty: Rubio/Thune-style +100.0 cells off one classified
  tweet are now impossible — the backend suppresses the net below n=5
  and the UI renders the honest n instead.

## Follow-ups

- `targetTone.collectives` (tone toward GOP/Democratic party as
  targets) is in the payload but not yet rendered; candidate replacement
  for the favorability-based "Tone toward GOP" ticker item.

Cross-link:
`docs/audit-trail/analysis/2026-07-10-target-sentiment-received-tone.md`.
