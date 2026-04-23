# 2026-04-22 — Hide Reddit from UI

The Reddit filter pill and prose references to Reddit-as-a-live-source were pulled from the UI. Historical Reddit documents in the database continue to render with their original source labels; users just can't scope or discover by Reddit anymore.

## What shipped

- `ui/src/components/common/GlobalFilters.tsx` — removed the `{ id: 'reddit', label: 'Reddit' }` entry from `SOURCE_TYPES`. The pill no longer renders on any page.
- `ui/src/pages/Home.tsx` — two prose edits: the lead paragraph and the "01 · Collect" step now list sources as "news sites and X" (was "news sites, Reddit, and X").
- `ui/src/pages/PublicSentiment.tsx` — `HowThisWorks` blurb updated: "news articles and X posts" (was "news articles, Reddit posts, and X posts").

## Retained intentionally

- `Filters.sourceType: 'all' | 'news' | 'reddit' | 'social'` union in `types.ts`.
- `SourceFilter` union in `services/api.ts`.
- `COLORS.sourceReddit` theme token + `reddit_post` / `reddit_comment` source-type handling in Narratives colour map, SupportingDocsTable label builder, and source-breakdown UI.

Existing Reddit docs in the database continue to appear in narratives, source-breakdown rows, and supporting-docs tables with the correct Reddit styling. Removing support would falsely rewrite history on surfaces that cite those docs as evidence — see the C1 invariant.

## Why

Paired with `../infra/2026-04-22-disable-reddit-ingest.md`: Reddit API access was withdrawn. The UI shouldn't offer a filter or advertise a source we're not actively ingesting.

## Follow-ups

- If Reddit returns: re-add the filter pill entry in `GlobalFilters.tsx` and reinstate the prose references in `Home.tsx` + `PublicSentiment.tsx`. No backend changes required.
