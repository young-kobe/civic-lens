# 2026-07-25 — Bot Detector's flagged-account field is a share, not a score

`FlaggedAccount.botScore` is renamed to `flaggedPostShare` throughout the
UI, matching the API's field rename
(`docs/audit-trail/api/2026-07-25-bot-exclusion-gate.md`). The value itself
also changed meaning server-side: it is now the share of an author's
confidence-floored analyzed posts labelled bot/suspicious by the model
(`bot_post_count + suspicious_post_count` over `sample_count`), not the
retired additive bot-likelihood score.

## What shipped

- **`ui/src/types.ts`**: `FlaggedAccount.botScore: number` renamed to
  `flaggedPostShare: number`.
- **`ui/src/pages/BotActivityProfiler.tsx`**: all three consumers updated.
  - `FlaggedAccountModal`'s stat tile: label text "Bot score" -> "Flagged
    post share"; value reads `account.flaggedPostShare`.
  - `FlaggedAccountsCard`'s per-row rate display (`botRateColor(...)` call
    and the rendered percentage) reads `a.flaggedPostShare`.
  - `BotActivityResponse.botScoredDocCount` is UNCHANGED -- a different
    field (a count, not this share) that the rename does not touch.
- No other UI file references `botScore`/`bot_score` (verified by repo-wide
  grep); `DigestSection.tsx`, `DataDesk.tsx`, and `services/api.ts` import
  `BotActivityResponse`-adjacent types but never read this field.

## Why

A field called `bot_score`/`botScore` holding a labelled-post SHARE would
misrepresent what the number is -- `.agent/rules/media-analysis.md`'s
labeling-discipline requirement (confidence/derivation must be legible next
to an AI-derived number) extends to the field's own name, not just its
displayed value. Silently redefining the field under its old name would
have been the dishonest option; renaming it makes the meaning change visible
in every diff and every API consumer.

## Follow-ups

None -- this is a mechanical rename with no remaining UI-side work. See the
API-layer entry's follow-up on validating the new threshold
(`BOT_FLAGGED_SHARE_EXCLUSION`) against production data once available;
that validation, if it changes the threshold, will not require any further
UI change since the UI only ever displays the resolved share value.
