# Walkthrough 054 — Entity registries (outlets, officials, subreddits)

Phase 2 of the UI Redesign Plan (`docs/ui-redesign-plan.md`). Pure content pass: three new YAML registries and a schema test. No code paths consume them yet — Phase 3 wires them into the aggregators.

The point of the registries is to promote a specific, auditable set of outlets / officials / subreddits to "first-class entity" status on the dashboard. Before this walkthrough, docs were aggregated by source type (news / reddit / x). After Phase 3, they'll be aggregated per entity *within* each type where a registry entry matches, with everything else bucketed into a catch-all.

---

## What changed

### `data/news_outlets.yaml` (new)

20 US-political news outlets per the plan's "Outlet coverage list": NYT, WaPo, WSJ, USA Today, AP, Reuters, NPR, BBC, Fox News, CNN, MSNBC, Bloomberg, Politico, The Hill, Axios, Atlantic, National Review, Federalist, Breitbart, Intercept.

Per entry: `domain`, `also_domains`, `display_name`, `blurb`, `partisan_lean`, `lean_source`, `owner`, optional `founded` + `circulation_note`.

- Partisan lean uses the 6-bucket enum from the plan: `left | center-left | center | center-right | right | mixed`.
- Lean citations are AllSides Media Bias Ratings 2024 where available. `lean_source` is a free-text citation — it will be displayed next to the lean label on every entity card, per the plan's "always display the citation" mitigation for the politically-sensitive labeling.
- `circulation_note` is explicitly labeled as a reach proxy in the file's `notes` header (matches invariant rule 2 in `.agent/rules/media-analysis.md`).

Two outlets are non-US by headquarters (BBC, Reuters). They stay in scope because US political discourse treats them as first-class sources; the `notes` block calls this out.

### `data/verified_officials.yaml` (new)

16 seats per the plan's "Officials coverage list": President, VP, Speaker, Senate + House majority/minority leaders, RNC + DNC chairs, Secretaries of State / Treasury / Defense / Justice / DHS / Health, and the FBI Director.

Per entry: `handle` (primary X handle with leading `@`), `also_handles`, `display_name`, `office`, `party`, `blurb`, `term_start` (ISO date), `bio_source` (Wikipedia canonical URL).

- Populated with the current Trump 47 / 119th Congress roster as of 2025-04-21: Trump, Vance, Mike Johnson, Thune, Schumer, Scalise, Jeffries, Whatley (RNC), Martin (DNC), Rubio, Bessent, Hegseth, Bondi, Noem, RFK Jr., Patel.
- The file's `notes` block calls out that the registry is a snapshot and should be verified before any time-sensitive publishing decision.
- `bio_source` is Wikipedia for every entry. Wikipedia is canonical, continuously updated, and doesn't require us to hand-maintain official-bio URLs that rotate with department reorganizations.

The plan asks for handles to also exist in the larger `data/known_political_x_accounts.yaml` registry (one source of truth for ingestion). Spot-checking: executive-branch + congressional handles are already there; the DNC chair and HHS secretary may need a follow-up add to `known_political_x_accounts.yaml`, which Phase 3 will enforce when it audits the actual ingest-to-registry match rate.

### `data/major_subreddits.yaml` (new)

10 political subreddits per the plan's "Subreddit coverage list": r/politics, r/Conservative, r/democrats, r/Republican, r/PoliticalDiscussion, r/Libertarian, r/AskConservatives, r/AskALiberal, r/neoliberal, r/PoliticalHumor.

Per entry: `subreddit` (no `r/` prefix), `display_name` (with `r/`), `blurb`, `tilt` (4-bucket enum: `left | center | right | mixed`), `tilt_source`, `subscriber_count_proxy`.

- Subreddit names are stored in their canonical Reddit casing. Match-time normalization is the aggregator's job (Phase 3 pre-check flags case inconsistency in `docs.domain_or_subreddit`).
- The file's `notes` block calls out that tilt is a community skew, not a declared editorial position, and that subscriber counts are reach proxies.

### `analysis/tests/test_entity_registries.py` (new)

Schema-only tests — validate structure without importing any app code:

- Each file parses as valid YAML.
- Each file has exactly the count the plan requires (20 / 16 / 10).
- Every entry has its required fields.
- `partisan_lean` / `tilt` / `party` values are drawn from the allowed sets.
- `domain` / `handle` / `subreddit` are unique within their file (case-insensitive for subreddits, matching Reddit's lookup behavior).
- Handles start with `@`.
- Outlet domains have no scheme, no trailing slash, are lowercase, and contain a TLD.
- `term_start` is ISO `YYYY-MM-DD`.
- `bio_source` is an `http://` / `https://` URL.
- Blurbs are between 80 and 1000 characters — catches both missing and "n/a"-style stubs, and overly-long editorial pieces.

22 tests total, all pass.

---

## Why these choices

**Why hand-write blurbs instead of pulling them from Wikipedia?** The plan's "Blurb voice" decision explicitly calls for a neutral, 2-3 sentence editorial voice that frames *why this entity matters in US political media*, not a generic "who they are" summary. Wikipedia lead paragraphs are written for the encyclopedic register, not the editorial register; copying them would be off-tone and also misattribution-risky. Hand-writing keeps the voice coherent across the registry.

**Why Wikipedia as `bio_source` when the blurbs are original?** The `bio_source` is a verification pointer, not a citation of the blurb text. If a reader (or a future reviewer) wants to audit the blurb, Wikipedia is the fastest way to cross-check the factual claims. Official government bios rot faster than Wikipedia does under administration changes; Wikipedia is the more durable target.

**Why 20 outlets and not more?** The plan's "Entity coverage gaps" risk section names the tradeoff: too few and important signal disappears into "Other"; too many and maintenance burden grows faster than marginal-signal returns. 20 outlets cover the bulk of sampled political news volume; if Phase 3's coverage diagnostic shows the catch-all above 30% of news docs, we'll expand.

**Why Wikipedia over AllSides for officials' bio_source?** AllSides doesn't rate individual officeholders, only outlets.

**Why separate YAML files instead of one combined registry?** Each file has a different shape (outlets have domains, officials have handles, subreddits have subreddit names) and a different maintenance cadence (outlets change ownership rarely; officials change with every administration; subreddits change tilt more slowly than people assume). Three files with shared schema conventions is easier to reason about than one union-typed file.

**Why are partisan_lean and tilt different enums?** `partisan_lean` has six buckets because outlets get finer-grained ratings (AllSides distinguishes Lean Left from Left). `tilt` has four buckets because subreddit-level classifications from aggregators / studies are coarser; adding fake precision would misrepresent the source data.

**Why `party: R | D | I | independent-dem | other` rather than free-text?** Every current entry resolves to R or D, but the schema has to accommodate RFK Jr. (who ran as independent before endorsing) and future independents (Bernie Sanders is an independent caucusing with Democrats, AOC-aligned progressives may run as independents in some seats). Enumerating the likely shapes keeps later aggregator logic simple.

---

## Verification

- `PYTHONPATH=. ./analysis/.venv/Scripts/python.exe -m unittest analysis.tests.test_entity_registries` — 22/22 pass.
- No app code consumes these files yet; broader test suite unaffected.

---

## Files touched

- `data/news_outlets.yaml` — new.
- `data/verified_officials.yaml` — new.
- `data/major_subreddits.yaml` — new.
- `analysis/tests/test_entity_registries.py` — new.
- `docs/walkthroughs/README.md` — index row for 054.
- `docs/ui-redesign-plan.md` — check off Phase 2 tasks.

---

## Follow-ups carried into later phases

- **Phase 3 pre-check** depends on these registries existing. Phase 3 will compute the registry-match rate against `docs.domain_or_subreddit` (news) and `account_profiles.handle` (X) and expand or fix domain/handle-normalization where the catch-all is too large.
- **Handle cross-reference** with `data/known_political_x_accounts.yaml`: the DNC chair and HHS secretary may need adds to the larger registry so the aggregator can actually match docs to them. Phase 3 will surface this when it runs the real match audit against the database.
- **Blurb review cadence**: quarterly. The first review falls in 2026-07; on any confirmed vacancy or ownership change we update sooner.
- **Expansion triggers**: if Phase 3's coverage diagnostic shows any catch-all bucket above 30% of its parent tier's volume, expand the relevant registry.
