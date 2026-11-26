# Candidate features

**Status.** Candidates. None is accepted or implemented. Each one waits for an investigation of its data, solo-maintainer review cost, and risk. Remove a feature when its investigation shows that it will not work, and record the reason in the feature reference that owns the data. Move an accepted feature into the [work queue](../roadmap.md#work-queue).

The list combines the product direction, the PR #634 council review of 2026-11-20, and the 2026-11-24 solo-maintainer architecture review. Every feature follows the [pipeline rules](pipeline.md#rules-for-every-connection): it shows what the records say and never states a conclusion.

Planes: **P1** (Deterministic Documentary Graph, code-approved at full scale), **P2** (Judged Cross-Document Links, bounded solo-maintainer review capacity), **P3** (Derived Aggregates and Statistical Patterns, rule-gated per release), **P4** (Public Serving Projections and UI). Size: **S** is days, **M** is one to two weeks, **L** is longer.

## High-leverage deterministic foundations (Plane 1, scales by code)

Build these first because they deliver high civic transparency at full congressional scale with zero per-record manual review bottleneck.

| Feature | Plane | Reader question | Data | Main risk | Size | Gate |
|---|---|---|---|---|---|---|
| Motion polarity and procedural effect on votes | P1 | What did a Yea vote actually do on this motion? | Clerk and Senate XML question templates, mapped to pass, amend, table, recommit, cloture, or waive rule | Ambiguous procedural phrasing falls back to review | S | XML template audit |
| Edition-bound sponsorships with shell-bill detection | P1 | Did this member sponsor the version that passed, or an earlier text? | `SponsoredOn` dates joined to bill edition dates plus substitute-text replacement check | Date gaps on historical bills; show "edition date unknown" | M | R-63 / action schema |
| How it passed (roll call, voice vote, unanimous consent) | P1 | Why is there no individual vote recorded for my member? | Bill actions and roll-call links (already in R-69) | Uncoded voice-vote strings in older action text | S | R-69 |
| Source coverage meter per official and issue | P1 | Are these counts low because the official was quiet, or because Civic Lens could not read their site? | Collection health and configured source status (R-56) | Misreading scraper outage as official inactivity | S | Prerequisite for P3 comparisons |

## How a connection is shown

Follow three-layer progressive disclosure ([reader](reader.md#three-layer-progressive-disclosure)): show plain language and one guardrail line on the card, interactive step-by-step path second, and audit details in the collapsed Audit Drawer. (Planes 2 and 4.)

| Feature | Plane | Reader question | Data | Main risk | Size | Gate |
|---|---|---|---|---|---|---|
| Plain limit line on each quote and vote pair | P4 | Does this vote show what they think about this part? | Vote roles (`WholeBillPackage`, `SpecificMotion`, `Procedural`) and motion polarity | Too much jargon on cards; test plain copy in 6-person think-aloud | S | Vote-role labels and think-aloud |
| First-class insufficient evidence state | P2/P4 | Is this link unproven, or wrong? | `ReviewReasonCode.InsufficientEvidence` on `Reject`, conditional reopen, candidate digest | Overuse avoids a call; show exact missing record | S | D-19 (accepted) |
| Provision span on a relationship | P2 | Which words of the bill does this quote refer to? | Bill text extractions cited by action revisions (`TargetPassageSpan`) | Span on an old edition after a code-approved update | M | D-20 (accepted) |
| Basis label in Audit Drawer | P2/P4 | Is this a documented citation or a text match? | `MatchBasis` in [connection model](pipeline.md#connection-model) | Readers confuse "documented" with "supports claim"; keep in Audit Drawer | S | Pilot |
| Sensitivity grid and pre-trend for numeric claims | P2/P4 | Does the result depend on the dates chosen, and was it already moving? | Single-series workbook cells, 12-month pre-trend, inflow/completion flow data | Large grid overwhelms cards; show plain summary first, grid in Audit Drawer | S to M | Single-series outcome check |
| Confidence bounds beside small counts | P3/P4 | How sure is a small count? | Existing counts and exact intervals | Readers read a bound as a forecast; place in Audit Drawer | S | None |
| Risk and coverage chart on the methods page | P4 | How often does Civic Lens decline to link, and how often is a link wrong? | Pilot outcomes, intra-rater kappa, and ongoing audit counts | Small sample read as a guarantee; show intervals and single-maintainer note | S | Pilot result |
| Replay status in Audit Drawer | P4 | Can someone else check this number? | Input bundle manifest and expected outputs | Status goes stale when upstream bytes change | S | Input bundle |

## Reader tools

Plane 4 reader tools.

| Feature | Plane | Reader question | Data | Main risk | Size | Gate |
|---|---|---|---|---|---|---|
| Site-wide correction and withdrawal log | P4 | What did Civic Lens get wrong, and when did it fix it? | Per-record corrections plus monotonic withdrawal tombstone ledger | Withdrawn entries must reveal none of the withdrawn content | S | Withdrawal tombstones |
| Copy link and citation in Audit Drawer | P4 | How do I cite this exact record? | Stable permalink, release number, revision, and as-of date | Replaces "Export to Obsidian" with a universal, correction-checkable link | S | None |
| New since your last visit, for issues and followed members | P4 | What changed since I last looked? | Release first-published dates (ZIP lookup already shows newly published votes) | Count republication once; stays parked during R-68 | S to M | R-68 closure |
| Shareable evidence preview link | P4 | How do I share this exact record? | Stable URL with server-updated Open Graph preview, release ID, and as-of date | Static images cannot be withdrawn; prefer live server previews | M | Withdrawal tests per surface |
| Atom feeds by member or issue | P4 | Tell me when my member votes on this issue | Published votes, issues, statements, and release dates | Delivered feed items cannot be recalled; include title and link only, no email | M | Withdrawal tests per surface |
| Promise and record page | P2/P4 | Did my member do what they said? | Dated commitments and later casts on the specific motion | Highest attribution risk and solo-maintainer review load; sparse page implies silence | L | Pilot passes, R-48, coverage meter |
| Ask about this record | P4 | What does this mean for me? | Published text only, with citations | AI text beyond evidence and per-query API cost | L | Do not build in this plan |
| Local impact by ZIP | P1/P2 | What did this law do in my town? | USAspending obligations and verified site completions | False local credit to a named official; FAIN missing in opening reports | L | 5-recipient delivery trial passes |

## New evidence sources

Each source follows R-58 (Planes 1 and 2): an official API or bulk file, with a terms and robots check first. Prefer Plane 1 deterministic linkages over manual-review-heavy sources.

| Feature | Plane | Reader question | Data | Main risk | Size |
|---|---|---|---|---|---|
| Cost estimates | P1 | What does this bill edition cost? | CBO cost estimates bound to exact bill edition | Estimate covers one edition only; bind the edition | S |
| Independent reviews | P1/P2 | What did independent reviewers find about this program? | GAO, inspector general, and CRS reports | Report scope can differ from the claim | S |
| Directives with deadlines | P1/P2 | Did a directed task get done? | Federal Register text, agency progress reports, follow-up orders | Missing report is not failure; show "no report found as of" date | M |
| Did the funded thing happen? | P1/P2 | Was money obligated, spent, and delivered at a site? | USAspending awards/transactions, recipient completion records, site inventories ([Deerfield trial](experiments/evidence-trials/results.md#3-from-a-charging-grant-to-an-operating-site)) | Wrong award-to-site match; requires similar-name decoy control | M |
| What the bill does, beside what its sponsors said | P2 | Does the text match the description? | Edition-bound provision text and the member's own quotation | Paraphrase judgment requires solo-maintainer review budget | M |
| Agency performance data | P2 | Did the outcome move? | Agency dashboards and workbooks with pre-trend and flow metrics | Definitions and inflows shift (Goodhart's Law); bind definition version | M |
| Lobbying disclosures | P1/P4 | Who lobbied on this bill? | Senate LDA filings | Reads as collusion; show no link to a member and wait for a wording rule | M |

## Derived insights

Plane 3 rule-gated insights computed per release.

| Feature | Plane | Reader question | Data | Main risk | Size |
|---|---|---|---|---|---|
| Changed record | P1/P2 | Did an office delete a press release, rewrite a quotation, or remove a position? | Deletion detection and document-change gate (R-13) | Page move or URL change looks like deletion | M |
| Typical wait for a bill stage | P3 | How long has a comparable bill usually waited? | Right-censored bill actions by chamber, Congress, and stage | Too few events; suppress percentiles below 10 events or 20 at risk | M |
| Votes that split officials on an issue | P3 | Which votes explain a split? | Dated casts in one chamber with motion polarity | Reads as an ideology score; show the underlying motions | M |
| Unusual activity on an issue | P3 | Which issue saw unusual legislative activity this week? | Distinct substantive decisions by event week and issue ([graph research](graph.md#which-ai-or-statistical-study-comes-next)) | Single-window anomaly (cannot use next-window replication); guard against recesses and backfills | M |
| Shared language | P2/P3 | Which offices use the same sentences, and who used them first? | Statement text (R-38) | Shared boilerplate looks like coordination; solo maintainer reviews each finding | M |
| Recurring cosponsor groups | P3 | Who keeps backing bills together on this issue? | Edition-bound sponsorships by date | Reads as a hidden bloc; must pass party/committee null, leave-one-out range, and next-window replication before any group label | M |
