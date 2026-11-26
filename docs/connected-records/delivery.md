# Phase 4. Interactive evidence route

R-68 already separates release publication from HTML generation. `PublicApi` serves server-rendered `PublicWeb` Razor pages from restricted `public_api` views, Cloudflare caches public HTML per release, and ZIP results remain private fragments. Preserve existing deep links, permalinks, and source labels. [Public site](../architecture/public-site.md), [search and read API](../architecture/search.md), and [publication](../architecture/publication.md).

## One route before a stack decision

Under accepted decision [D-24](../roadmap.md#pending-decisions), **an independently deployed frontend shell is no longer the proposed destination.** A second runtime adds a deployment, a versioned read contract, cache invalidation ownership, and solo-maintainer operational drag with no measured failure of the current stack.

Build the Phase 4 trial directly on the existing `PublicApi` + `PublicWeb` Razor SSR stack, enhanced with progressive JSON-backed interactive panels from existing read routes. Choose one issue with an eligible reviewed finding (selected by the published deterministic rule, not hand-picked) and use the three-layer progressive disclosure layout ([reader](reader.md#three-layer-progressive-disclosure)).

| Approach | Role in this plan | Tradeoff |
|---|---|---|
| Existing `PublicApi` + `PublicWeb` Razor SSR with progressive JSON-backed panels | **Default path (D-24).** Server-renders navigation, Layer 1 answer, guardrail, and Audit Drawer HTML; small JS modules expand interactive steps, compare cutoffs, or toggle the local graph without a full-page refresh. | Zero new deployment or runtime. Keeps one architecture boundary and one solo-maintainer release path. |
| Independently deployed frontend shell | **Deferred.** Reopen only if a measured reader requirement fails on `PublicApi` + `PublicWeb` (for example, verified mobile latency or interactive state needs that Razor plus progressive JS cannot meet). | Requires a separate deployment, versioned read contract, rollback coordination, and independent withdrawal purge checks. Must follow the Public MCP precedent (`Publication.Contracts` only). |

Keep selection, eligibility, and withdrawal checks in one server-side owner (`PublicApi` over `public_api` views and the release-independent withdrawal tombstone ledger). Include release ID, record revisions, evidence cutoff, dated finding, source links, review state, and correction or withdrawal status in the payload. Never put database access in the browser. Request one bounded page payload where possible. If an interactive panel observes a different release than the initial page, retry coherently against the active release or mark the panel temporarily unavailable. No component may turn a failed read into a zero count.

## Acceptance trial

Run these checks on the first interactive Evidence Path route before rolling the pattern to additional issues:

- **Load and no-script baseline.** Load a cold deep link, a warm cached route, the JavaScript-disabled page, and an empty issue. Navigation, Layer 1 headline, guardrail, and one-step primary source links **must render with JavaScript disabled**. Record time to meaningful content on mobile (360 px), transferred bytes, API request count, and server p95.
- **API or panel failure states.** Make the panel JSON endpoint unavailable in development. Navigation, Layer 1 reading content, and methods must remain intact. Distinguish loading, unavailable, genuinely empty, insufficient evidence, and withdrawn states cleanly; never show a zero count on failure.
- **Monotonic withdrawal and rollback.** Withdraw a dependent record in a test release, then roll back to an earlier release and roll forward. Verify that `WithdrawalCascade` blocks active-release dangling citations and that the release-independent withdrawal tombstone ledger prevents rollback from resurrecting the withdrawn record on Home, the issue page, detail page, interactive panel, search, old permalink, and share preview. Meet a measurable **15-minute purge bound** across shared caches. Prevent mixing revisions from different releases.
- **Privacy and accessibility.** Verify that public HTML and shared caches contain only published material, keeping ZIP and personal state private. Pass keyboard-only, screen-reader (`aria-current` accurate), 360 px mobile answer-first order, and light/dark theme checks matching the [reader trial](reader.md#decision-test).

Keep the static/plain Razor view if the interactive panel fails privacy, withdrawal, or release-consistency checks, or if the measured benefit does not justify the added JS complexity. Do not add a new graph engine, ML service, or frontend framework to run this trial.
