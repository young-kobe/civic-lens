# 2026-04-22 — Reworded reads-as-today banner on Tone + Narratives

The "reads-as-today" banner on the Overall Tone and Political Narratives pages now reads as static editorial framing instead of a templated metric summary. Propaganda and Bot Detector banners are unchanged — their templated sentences still name a specific observation (top flagged outlet, automation-rate band) that reads as signal, not jargon.

## What shipped

- `ui/src/pages/PublicSentiment.tsx::readsAsToday` — returns a single static sentence: *"How news outlets, public officials, and everyday people are reading American politics."* No more per-tier net-tone templating, no more "dominant divergence" fallback.
- `ui/src/pages/Narratives.tsx::readsAsToday` — returns: *"The recurring talking points we've picked up across coverage."* No more "Most claims (X of Y)..." counts, no more "cross ≥ 2 tiers" notation.

Both functions kept as pure functions with the same signature so the page render path and eyebrow (`As of last N days`) above the banner stay identical. The eyebrow still provides the time-window context; the body is now purely framing.

## Why

User: *"the current wording is too verbose and internal metric worded. i like the banner aesthetic here just not the application for these two tabs."*

The templated sentences leaked internal vocabulary ("tiers", "claims", "dominant divergence", the ≥ glyph) into a banner that everyday readers skim first. The grid + divergence panel + cross-tier panel below each banner already surface the counts in shapes built for them. The banner's role is *framing*, and a static sentence does that better than a dynamic one that tries to double-duty as a headline.

## Decision: static vs. LLM-generated

Earlier the user asked whether the analysis layer should generate these one-liners. We agreed not — LLM summaries collide with the project's core invariant ("never fabricate") unless gated by a structured-output pipeline + validator that every number in the sentence comes from the input. That's infrastructure, not a banner. Static sentences stay honest by construction. If richer language becomes worth the cost later, the future path is an LLM phrase-selection pass over a curated library with the measurements bound as variables, not free-form generation.

## Follow-ups

- None. Propaganda + Bot banners kept their templated phrasing because those already name a specific, bandable observation (top flagged entity, automation-rate band) without leaking internal jargon.
