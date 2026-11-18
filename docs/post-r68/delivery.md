# Phase 4. Independent frontend delivery

R-68 already separates release publication from HTML generation. PublicApi serves Razor pages from restricted published views, public HTML can be cached, and ZIP results are private fragments. Uncached routes can still depend on the operational database being available. Preserve the current deep links and source labels. [Public site](../architecture/public-site.md), [search and read API](../architecture/search.md) and [publication](../architecture/publication.md).

## One route before a stack decision

Try the veterans issue page first. Serve its navigation, layout and loading states independently of content reads. Populate findings from the public API, with meaningful first HTML for shared URLs when published content is available. An interactive panel can load the timeline, compare two cutoffs or expand evidence without a full-page refresh. Never put database access in the browser.

| Approach | What the trial can test | Cost |
|---|---|---|
| Existing Razor plus API-loaded panels | Fastest route to testing the new information architecture and graceful panel failures | Separate UI deployment still requires packaging or hosting work. |
| Independently deployed shell with server-rendered reading pages | Cacheable first HTML, stable links and faster UI iteration. Proposed destination. Razor and other runtimes can implement it. | Requires a versioned read contract, compatible rollouts, invalidation and operational ownership. |
| Client-rendered SPA | Static shell delivery and persistent comparison tools | Measure initial load, no-script reading experience, accessibility and search indexing for shared records. |

Use the published view and release contracts as precedents. Decide the route response and compatible API version before adding dependencies. Include release ID, record revisions, evidence cutoff, dated finding, source links, review state and correction or withdrawal status. Keep selection and eligibility in a single server-side owner. Request one bounded page payload where possible. If separate panels observe different releases, retry coherently or mark the panel temporarily unavailable. No component should turn a failed read into a zero count.

## Acceptance trial

- Load a cold deep link, a warm cached route, the no-script page and an empty issue. Record time to meaningful content on mobile, transferred bytes, API request count and server p95 next to the equivalent Razor route.
- Make the API unavailable in the development trial. Navigation and methods should render. Distinguish loading, unavailable, genuinely empty, and withdrawn. Reuse cached evidence only when the publication policy can still honor withdrawals. Otherwise show the shell and availability state.
- Withdraw a dependent record in a test release, then roll back and roll forward. Check the homepage, issue finding, detail page, old URL and client caches. Define a measurable purge bound before shipping. Prevent combinations of revisions from different releases.
- Test deployment of a new UI against the old API and the old UI against a compatible new API. Exercise an incompatible contract in staging and recover by rolling back the route alone.
- Verify that public HTML and shared caches contain only published material. Keep ZIP and personalized fragments private. Compare keyboard, screen-reader and narrow-screen results with the [reader trial](reader.md#decision-test).

Decide after measuring whether the separate deployment adds enough value to justify its maintenance. A new graph engine, ML pipeline, or frontend framework is a separate architecture choice. None is required to run this trial.
