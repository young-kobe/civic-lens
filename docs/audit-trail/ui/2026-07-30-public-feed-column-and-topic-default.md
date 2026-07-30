# 2026-07-30 — Public column becomes a post feed; topic filter defaults to All Topics

The sentiment page's "The Public" column now renders a paginated feed of twitter-style post cards (`PublicPostFeed.tsx` consuming `GET /public-posts` — see `docs/audit-trail/api/2026-07-30-public-posts-feed.md`) instead of per-account rollup cards, and the topic tab bar defaults to All Topics and stays where the reader puts it. The other three split pages (Bots, Propaganda, Narratives) keep account rollups by design — their public columns express per-account flagged rates, which posts can't.

## What shipped

- `pages/publicSentiment/PublicPostFeed.tsx`: Load-more accumulation (house `btn btn-secondary` pattern with `N of total`), reset on `(window, topic)` change, a monotonic request token so stale responses can't append, and the sampling note ("ordered by engagement — a reach proxy, not verified audience. A sample, not the full corpus."). Cards are the existing `PostCardList` / `sampleToPostCard` path, so every card carries label + confidence.
- `PublicSentiment.tsx::SentimentThreeWayGrid`: third column renders the feed (via `ThreeWayColumn`'s children path, so it shares the bounded column scroll region) with the existing "Who the public is talking about" footer; the byline states the active topic and, when a lean pill is active, that the lean filter applies to the news/officials columns only (feed items carry no curated lean).
- Topic default: `pickDefaultTopic`, its arming effect, and the `pickedDefault` guard are deleted. No URL param now means All Topics, `writeTopicToUrl`'s delete-param-for-'all' behavior is correct rather than a trap, and clicking "All Topics" no longer snaps back to the most-discussed topic.
- `types.ts::PublicPostsResponse`, `services/api.ts::fetchPublicPosts`.

## Why

- The public tier's rollups usually collapsed into one pooled "Other X users" card — near-zero information. The feed shows the discourse itself, topic-filtered server-side.
- The auto-default was two bugs in one: a fresh visit never landed on All Topics, and the guard re-armed whenever the param was absent, making All Topics unselectable.

## Follow-ups

- Visual pass on a live dev DB (no `.env`/database in this working copy at build time): feed pagination, topic switching, and the footer at 375px.
