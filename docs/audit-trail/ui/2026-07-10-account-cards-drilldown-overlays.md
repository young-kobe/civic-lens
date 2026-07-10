# 2026-07-10 — Account cards, live drill-down, received-tone breakdowns, agreement chip

## New entity kind: `account`

`EntityProfile.kind` / `EntitySentimentItem.kind` (types.ts) gain
`'account'` — curated politically-classified X authors that aren't in the
editorial registry. `EntityProfileCard` renders them like officials
(unavatar avatar, x.com outbound link, party chip); the modal subtitle
shows office/party/account-type. The Public column byline now says
"Political subreddits, curated political accounts, and X users we don't
track individually". `PropagandaEntityItem` / `BotEntityItem` kind unions
widened to match (those aggregators don't emit accounts yet).

## Entity modal (Overall Tone)

- **Live drill-down** — "Show all N classified posts" pages the full list
  from `GET /entity-posts` (50/page, Load more), replacing the previous
  hard ceiling of ~10 cached samples. Loaded lists are newest-first and
  labeled as such; the cached fallback is now honestly titled
  "Highest-confidence classified posts" (it was labeled "Recent" while
  being confidence-sorted).
- **Received tone breakdowns** — "Who is talking about X" (by speaker
  tier) and "Narratives driving these mentions" tables, plus a
  "weighted by engagement" line under the received net with the formula
  in the tooltip and a proxy disclaimer. All cells render "low sample"
  instead of a number when the backend suppresses them.

## Narratives modal

"Citation edges" section: inbound count split by link type
(link/quote/reply/retweet), external un-ingested link-outs, and top
cites-into / cited-by narrative pairs — with an explicit note that edges
connect sampled documents and are not origin/propagation claims.

## Human-agreement chip

The Overall Tone header shows "Human review agreement on tone
classifications: N% across M reviewed outputs" from `GET /eval-accuracy`,
rendered only when the server publishes a percentage (enough scored
reviews); silent otherwise.

Cross-links: `docs/audit-trail/analysis/2026-07-10-entity-accounts-and-drilldown-reads.md`,
`docs/audit-trail/api/2026-07-10-drilldown-and-eval-endpoints.md`.
