# Sentiment provenance edges + plain-language sweep + analysis explainer

Owner request 2026-07-27: (1) received-tone metrics must show WHO the tone
comes from (parties, outlets, sampled X users) wherever they render —
completing the sentiment graph alongside the existing outbound view; (2)
simplify statistical language app-wide; (3) a layman front-page explanation
of the LLM analysis layer, with tooltips where density is irreducible.

Contract decided up front (plan file 2026-07-27): `ReceivedTone` gains
`receivedFromGroups` (source_class x lean rollups, raw values; labels built
UI-side) and `receivedFromTop` (top 8 named sources, registry ones with
embedded entity profiles). Shares sum to 1.0 over received volume with an
explicit "other" bucket; per-cell net withheld below MIN_TARGET_SAMPLE_N.
Grouping axis is `corpus.entities.lean` (flat enum), never `lean_source`.

- [x] Wave 1 backend: source-bucket accumulator in `_accumulate_received`
      (mirror of `_route_outbound_bucket`), e_out/e_sub/e_auth lean joins in
      `_TARGET_ROWS_SQL`, widened profile fetch, groups/top emission, unit +
      PG-gated tests, snapshot re-record incl. populated-received fixture
- [x] Wave 1 UI language: Home "How it works" expanded into the layman
      analysis explainer; glossary +~10 entries; DefinitionChip rollout over
      load-bearing hover-only titles; jargon fixes (template_score, member
      docs, n=, offenders, UTC, raw taskType enums, scanned/scored unified);
      dead shared ConfidenceBadge deleted
- [x] Wave 2 UI provenance: types.ts mirror, "Where this tone comes from"
      block in EntitySentimentModal, EntityProfileCard "mostly from" line
      (now a displayed sub-line, not just a hover tooltip), PublicSentiment
      jargon pass (log-formula hover -> glossary entry, divergence subtitle,
      pts -> points, Dem/GOP spelled out)
- [x] Gate: full Python suite gated (892 tests, twice) + ungated (892, 284
      skipped) green, sentiment snapshot byte-stable twice (sha256
      `8a96d6a8...` both runs), ui typecheck + build green
- [x] Audit-trail entries (api + ui) written, this todo ticked
- [ ] Owner eyeball + commit (Kobe runs all git)

Follow-ups found during integration (2026-07-27), not built now:
- [ ] Party-collective received-tone provenance has no UI surface yet.
      `TargetToneMeta.collectives` (gop_collective/dem_collective) carries
      the same `receivedFromGroups`/`receivedFromTop` fields as an
      official's `received` block (backend emits it uniformly via
      `_format_received`), but `PublicSentiment.tsx` never renders
      `targetTone.collectives` at all -- this predates this wave. Needs its
      own panel, not a reuse of `ReceivedProvenanceBlock` as-is (no single
      `EntitySentimentItem` to attach it to).
- [ ] `GlobalTicker`'s `TickerItem.label` is now `ReactNode` (widened for
      the plain-language pass) -- the Saturation ticker label on
      Propaganda/DataDesk could take a `DefinitionChip` the same way
      `NarrativeLifecyclePanel`'s confidence badge does, if wanted.

Deferred (not built now): per-sampled-user derived lean on provenance cells
via `analysis.author_leans` — revisit if the "sampled X users" group proves
too coarse.
