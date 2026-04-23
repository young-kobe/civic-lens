# Walkthrough 056 — X official-timeline query coverage

Addresses the pre-check blocker from walkthrough 055: 0 of 2,784 X docs matched `verified_officials.yaml`. The ingest runs topic queries only, so posts authored by tracked officials never appeared in the sample.

This walkthrough fixes that with a **zero-Go-code** change: two new query strings in `seeds.yaml` that use X search's `from:` operator to pull official timelines, plus a modest budget bump to fit them.

---

## What changed

Only `data/seeds.yaml`. No Go code, no migrations, no new endpoints.

### Query composition (new "option C hybrid" shape)

1. **Eight topic queries** — unchanged from walkthrough 048. These keep measuring what the broad sample audience is posting about.
2. **Pure-timeline query for 9 high-profile officials**:
   ```
   from:POTUS OR from:VP OR from:realDonaldTrump OR from:JDVance
   OR from:SpeakerJohnson OR from:RepJeffries OR from:SteveScalise
   OR from:LeaderJohnThune OR from:SenSchumer
   OR from:ChairmanWhatley OR from:TheDemocrats
   ```
   Every hit is authored by one of these 9 officials, regardless of topic. Covers President + VP + congressional leadership (both parties, both chambers) + party chairs (institutional DNC handle in place of Ken Martin's personal account; @ChairmanWhatley for RNC).

3. **Cabinet × topic intersection** for the 7 cabinet secretaries + FBI director:
   ```
   (from:SecRubio OR from:SecScottBessent OR from:SecWar
    OR from:PeteHegseth OR from:AGPamBondi
    OR from:Sec_Noem OR from:KristiNoem
    OR from:SecKennedy OR from:RobertKennedyJr
    OR from:FBIDirectorKash OR from:Kash_Patel)
   (GOP OR Republican OR Trump OR MAGA OR Biden OR Democrats
    OR Congress OR Senate OR abortion OR SCOTUS
    OR immigration OR border OR inflation OR economy OR guns)
   ```
   Only posts by these 7 officials that touch one of the topic axes. Narrower per-author coverage than the pure timeline, but higher per-post signal for the three-way topic-divergence panel. Official + personal handles are OR'd together per official.

Both queries fit under the 512-char limit on X search's basic tier (timeline ≈ 209 chars, intersection ≈ 370 chars).

### Budget adjustments

- `max_tweets_per_query`: 10 → **8**. Modest cut across all queries to fit the 3 extra queries (11 total) under the new cap.
- `monthly_budget_cents`: 2500 → **3000**. Bumps the $25 ceiling to $30 to give headroom above projected spend.

Projected cost:
- 11 queries × 8 tweets × 30 days = 2,640 tweets/month.
- At the ~$0.0104 blended rate from walkthrough 048 (tweets + user hydration amortized) = **~$27.50/month**.
- Well under the new $30 cap. The $2.50 headroom absorbs day-to-day variance in unique-author counts.

Cycle remains **once/day at 04:00 UTC** per `deploy/systemd/civic-lens-x.timer` — no change.

---

## Why these choices

**Why `from:` operators in search queries instead of a new user-timeline endpoint?** The user-timeline endpoint (`GET /2/users/:id/tweets`) gives deeper per-official pulls and cleaner pagination, but at the cost of a new Go method, a handle→user_id resolution step, new budget accounting, and tests. The `from:` operator on the existing search endpoint delivers the same signal Civic Lens needs for Phase 3b today without any of that — it's a seeds.yaml change only.

The deep-pull user-timeline path is worth the work **if** option-C intersection queries don't populate the Officials column well enough. That question gets answered by data, not speculation. Tracked as a future TODO in `docs/ui-redesign-plan.md`.

**Why the hybrid (C) instead of pure-intersection (B)?** B limits every official's coverage to posts that touch one of the 8 topic axes. For high-profile officials who post frequently off-axis (e.g. Trump personnel announcements, Vance foreign-policy speeches), B leaves the Officials card feeling curated/incomplete. C covers the top 9 officials with pure timelines so their cards carry full-voice signal and puts the 7 cabinet/FBI officials on intersection queries where narrower coverage is acceptable because their media profile is narrower.

**Why pick those 9 for the pure-timeline and those 7 for the intersection?** The 9 chosen for pure timelines are the officials with the highest political-media volume: president + VP + top-four Senate + top-four House leaders + party chairs. Cabinet secretaries + FBI director have lower per-official volume and mostly surface in coverage when they intersect one of the topic axes — so the intersection is both cheaper on budget and better-aligned to the audience question ("what does the cabinet say about immigration?").

**Why bump the budget cap to $30 and not just trim the ingest harder?** The overall operating-cost target in `seeds.yaml` is still "~$50/month total". X going from $25 → $30 ceiling ($27.50 projected) leaves us comfortably within that envelope. A tighter ingest would underserve Phase 3b's Officials column on day one.

**Why drop `max_tweets_per_query` for existing topic queries too?** The alternative was to give the 3 new queries a smaller per-query cap and leave topic queries at 10. Cleaner to apply one global cap than split-tune per query — the data loss on topic queries is small (from 10 to 8 samples per query × 30 days = 60 lost tweets/query/month) and the cost-model stays simple.

**Why @TheDemocrats (institutional) instead of @kenmartinmn (personal)?** Party-chair personal accounts tend to post at low volumes, while the institutional party accounts post daily talking-point content at much higher volumes. The institutional handle is a better proxy for the party's "voice" than the chair's personal handle for aggregate sentiment / stance measurement. Symmetrically for RNC we kept @ChairmanWhatley + could add @GOP if the Whatley handle resolves poorly — tuned on data review after the first week.

---

## Verification plan (post-deploy)

No deploy in this walkthrough. When deployed:

1. Trigger the ingest manually or wait for the 04:00 UTC cycle.
2. After first run, check:
   ```sql
   -- Are any from:-based docs landing?
   SELECT u.username, COUNT(*) as n
     FROM docs d
     JOIN x_posts_raw x ON x.tweet_id = d.ident
     JOIN x_users_raw u ON u.user_id = x.author_id
    WHERE d.source_type='x_post' AND d.fetched_at > strftime('%s','now','-1 day')
    GROUP BY u.username ORDER BY n DESC LIMIT 30;
   ```
3. Which of the 16 official handles appear? If any are 0, either (a) the handle string in `verified_officials.yaml` is wrong, or (b) the handle is correct but the account hasn't posted in the recent-search window (~7 days). Check a few on x.com to disambiguate.
4. Run the coverage diagnostic from walkthrough 055 and confirm registry-match rate > 0%.
5. Check `x_api_budget` projected monthly spend after the first week: should be tracking toward ~$27.50. If higher, tighten `max_tweets_per_query` or drop the lowest-value topic query.

If any registry handles consistently miss, fix `data/verified_officials.yaml` and the corresponding `from:X` clauses in `seeds.yaml`.

---

## Files touched

- `data/seeds.yaml` — 2 new political_queries; `max_tweets_per_query` 10 → 8; `monthly_budget_cents` 2500 → 3000.
- `docs/walkthroughs/README.md` — index row for 056.
- `docs/ui-redesign-plan.md` — reference this walkthrough; add the per-official deep-pull follow-up TODO.

---

## Follow-ups

### Per-official deep-pull endpoint (pending Phase 3b validation)

Add `GET /2/users/:id/tweets` support in the X client once we've seen whether option-C intersection queries populate the Officials column well enough. Rough shape:

- New method on `ingest/internal/extract/x/x.go` Client: `GetUserTimeline(userID string, maxResults int)`.
- Handle → user_id resolution via `GET /2/users/by/username/:handle` (one-shot per official, cache in `verified_officials.yaml` or a new table).
- Per-official pull budget (e.g. `x.per_official_timeline_tweets: 15`) separate from `max_tweets_per_query`.
- Per-cycle walk over the 16 officials list in `verified_officials.yaml`.
- Extra cost: 16 officials × 15 tweets × 30 days = 7,200 tweets/month ≈ $75/month. Requires a budget strategy discussion (drop topic queries? accept a higher monthly ceiling?).

Gate: ship Phase 3b aggregators first with option C; measure Officials-column coverage for two weeks; decide whether deep-pull is worth the added complexity + cost. Captured as a TODO in `docs/ui-redesign-plan.md`.

### Tuning after first-week data

- Drop or merge topic queries that overlap heavily (e.g. `GOP OR Republican` and `Trump OR MAGA` co-occur on ~30% of posts in the current sample — candidate for combination).
- Add `from:GOP` to the timeline query if `from:ChairmanWhatley` returns 0.
- Consider split-tuning `max_tweets_per_query` per query shape once we have spend data.
