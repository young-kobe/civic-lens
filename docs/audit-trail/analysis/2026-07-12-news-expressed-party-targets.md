# 2026-07-12 — News cards carry expressed party-stance labels

News entity cards on the Tone page now show the outlet's EXPRESSED stance toward each party — e.g.
"about Democrats (party) · negative · about Republicans (party) · positive" — the same
outbound-target read the public cards already carry. Previously "who they're talking about" was
computed for public-tier entities only.

## Backend (analysis)

- **`sentiment/target_tone.py`** — `_merge_outbound_targets` now attaches `outbound` to **news
  outlets** as well as public buckets. The mention loop routes each target_mention to its authoring
  bucket by tier: news → the outlet domain (folding to `CATCH_ALL_OUTLETS`), public → the existing
  subreddit/handle/catch-all logic; the accumulator + attachment are keyed by `(tier, bucket_key)`
  and write to `byNewsOutlet` or `byGeneralPublic` accordingly. Party collectives keep their fixed
  labels ("Democrats (party)"), so a news outlet's stance toward each party surfaces as an outbound
  target with a suppressed-below-floor net.
  - Officials are still excluded here (their toward-others signal is
    `expressed_alignment`). Existing public-outbound behavior is unchanged (target-tone tests green).
  - Note: the news branch mirrors the tested public branch; a dedicated news-outbound intent test is
    a follow-up.

## Frontend (ui)

- **`PublicSentiment.tsx`** — `newsReadsAs(item)` builds the card's `Reads as:` line for `outlet`
  cards from its outbound party collectives (`stanceWord()` → plain positive/negative/neutral);
  `readsAsFor()` routes officials → topic read, news → this new party read. The entity modal already
  renders `item.outbound` for any item, so clicking a news card now shows its "Who they're talking
  about" section for free.
- **`fixtures.ts`** — `mockOutbound(demNet, repNet)`; NYT and Fox mock outlets seeded with opposing
  party stances so the labels render in fixtures.

## Also — Propaganda: by-party folded into "Techniques being used"

Three cards on one row cramped them, so the standalone "Persuasion techniques by party" card was
**merged into "Techniques being used"** as a "By party · tracked officials" section under the
technique bars (`TechniqueExplorer` gains a `parties` prop + `ByPartySection`; the old `ByPartyCard`
and its `PARTY_ACCENT` moved out of `Propaganda.tsx`). The top row is back to `col-span-5 / 7`
(metrics + the combined techniques card).

## Verification

- Backend target-tone/sentiment tests green (32); UI `typecheck` + `build` green. Real data populates
  news outbound after the next `save_snapshots`; fixtures show the NYT/Fox labels now.
