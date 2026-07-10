# UI depth overhaul — page identity, evidence rendering, Data Desk

Adversarial UI review (2026-07-10) found the four data pages structurally identical
(same ticker → headline → metrics → three-way card grid skeleton), the app's only
chart hidden in a modal, cached data (`gopTrend`, `distributionSamples`,
`byDayOfWeek`, narrative `timeline`) never rendered, definitions trapped in hover
tooltips, and post evidence rendered as dry table rows. Full plan context lives in
the PR descriptions; decisions fixed with Kobe: self-rendered post cards (no embed
widgets), full-stack phased, general-reader-first with density one click deeper.

Overlap note: shipping this initiative also closes several boxes in
`ui-consistency-audit.md` (stat-row unification, confidence chip consolidation,
Propaganda `ExampleRow` refactor) — tick them there when the relevant box here ships.

## Phase 1 — UI-only (data already in cache)

- [x] PostCard + PostCardList (`components/common/PostCard.tsx`): x/reddit/news
      flavors, inline evidence-span highlighting with quoted fallback, confidence
      badge, reasoning disclosure, permalink out; adapters from
      ClassificationSample / SupportingDoc / FlaggedExample / PropagandaExample.
- [x] Replace SupportingDocsTable, FlaggedPostList, and Propaganda ExampleRow with
      PostCardList; delete the replaced files once unreferenced.
- [x] Glossary (`services/glossary.ts`) + DefinitionChip (touch-accessible term
      popover); plain-language reframing of mean score (saturation levels) and
      coordination index (lockstep levels).
- [x] EntityStat mini axis bars on EntityProfileCard stats (visual anchor for
      net-tone / rate numbers).
- [x] deepLink service (`services/deepLink.ts`) + `App.tsx` `#tab?param` parsing;
      migrate Tone's `?topic=` search param to hash params with legacy fallback.
- [x] RankedEntityList; Propaganda + Bots grids switch to it; Narratives cards go
      compact (no blurb); Tone keeps full profile cards.
- [x] Tone signature: ToneTrendPanel (gopTrend daily chart + weekday strip),
      TopicDivergencePanel promoted below headline, IntensityMini segments open
      distributionSamples as PostCards; officials' received-tone tables become
      bar-row lists with top-1 line surfaced on the card.
- [x] Narratives signature: NarrativeLifecyclePanel (top ~8 timelines as rows,
      first-seen tier dot, tier chips); grid demoted; cross-narrative citations
      clickable.
- [x] Propaganda signature: TechniqueExplorer (select technique → its flagged
      posts with highlighted evidence).
- [x] Bots signature: CoordinationEvidencePanel funnel (scanned → flagged →
      coordination level → top domains), replacing BotOverviewMetrics.
- [x] Data Desk tab (`pages/DataDesk.tsx`): cross-signal entity matrix, movers
      board, small multiples, pipeline health + eval accuracy.
- [x] Home live digest: tier tone rows, top narratives, MoversTicker, propaganda +
      bot tiles; prose condensed.
- [x] ThreeWayColumn "Show all (N)" + sort toggle.
- [x] Fixtures extended (non-trivial gopTrend / distributionSamples / byDayOfWeek /
      timeline); typecheck + build + unittest pass; dead-code sweep.

## Phase 2 — aggregator enrichment (each independent)

- [ ] `toneTrend[]` per-tier daily series in `sentiment_{window}` (nulls below
      sample floor render as gaps).
- [ ] Engagement counts on samples (x likes/retweets/replies/quotes; reddit
      score/num_comments) in sentiment + narrative samples and /entity-posts.
- [ ] X author enrichment on samples (avatar_url, verified_type, followers_count,
      account_created_at); reddit author stays null — never fabricate.
- [ ] Per-example bot evidence (confidence, sanitized indicators, reasoning) on
      FlaggedExample.
- [ ] Outlet profiles wiring: window support in `outlet.py`, `outlet_profiles`
      snapshot key, `GET /outlet-profiles`, Tone-page tone-by-bot-rate panel.
- [ ] Backend tests for each new field; fixtures updated; stale-cache tolerance
      (all new fields optional).

## Phase 3 — cross-links + polish

- [ ] Entity hub link row in every entity modal ("See this entity on ...");
      Propaganda + Bots resolve `entity=` params.
- [ ] Client-side entity/narrative search.
- [ ] TopicDivergencePanel rows self-link `#sentiment?topic=`; `byTimeWindow[]`
      freshness strip.
- [ ] Retire `isNoiseLabel` display filter once post-sanitization snapshots cycle;
      drop unavatar.io if author enrichment coverage suffices.
