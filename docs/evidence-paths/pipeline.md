# Relationship pipeline

**Status.** Planned. Nothing on this page is implemented, except the existing owners that it names as precedents.

One product model connects records across many dimensions: officials, statements, bills and provisions, votes and casts, sponsorships, actions, executive actions, document changes, and outcomes. It aggregates them by official, issue, bill family, committee, and time. The public site shows them in maps, lists, timelines, and pages. The site shows what the records say and how they connect. It never states a conclusion.

Under the hood, four distinct data planes implement this product model so that a solo maintainer can scale Civic Lens without turning human review into a bottleneck. A pipeline that searches many dimensions can find patterns by chance or turn a true fact into a false impression, for example showing a vote on a whole bill as a position on one provision. The rules below incorporate the [trial results](experiments/evidence-trials/results.md), the PR #634 council review of 2026-11-20, and the 2026-11-24 solo-maintainer architecture revision. They apply to every connection type, not only to the first pilot.

## Four data planes

All four planes share one **Evidence Reference** contract: `(RecordKind, RecordId, RevisionNumber, OptionalSpanOrCell, AsOfUtc)`.

| Plane | What it owns | Approval and scale | Existing owner or precedent |
|---|---|---|---|
| 1. Deterministic Documentary Graph | Typed edges backed by official identifiers and verified citations: casts on votes (with motion polarity and procedural effect), votes on bill editions, edition-bound sponsorships (with `SponsoredOn` and shell-bill detection), voice/UC passage method, amendments, committee actions, related bills, Federal Register directives, and USAspending FAIN transactions. | Code approves after citation checks. Scales to 100% of the corpus with zero LLM spend and zero per-record human bottleneck. | [Collection](../architecture/collection.md), [bills](../architecture/bills.md), [votes](../architecture/votes.md), R-58 and R-69 in [Evidence Paths](../evidence-paths.md#accepted-plans) |
| 2. Judged Cross-Document Claims and Links | Cross-document links that require judgment: statement span to bill provision span, statement to vote with role, provision mechanism survival across editions, statement to outcome series, and award to local site when no FAIN links them. Candidate retrieval runs asynchronously in a bounded worker stage, never inside synchronous `AfterDecision` review-lock transactions. | Solo maintainer approves every judgment link inside a bounded weekly review capacity. Models propose candidates and run devil's-advocate checks only; a model prediction is never an approval. | [Relationship drafting](../architecture/statements.md#relationship-drafting), [review](../architecture/review.md) |
| 3. Derived Aggregates and Statistical Patterns | Release-scoped counts, rates, base rates, stage-reached distributions, bill wait curves, activity anomalies, and recurring cosponsor groups. | Recomputed deterministically per release and gated by explicit statistical rules. Never enters the manual review queue. | Boards and release counts in [publication](../architecture/publication.md) |
| 4. Public Serving Projections | Release-consistent reads through `public_api` views using correlated `LATERAL` queries, plus optional revision-ranged projection rows in `CivicLens.PublicApi`'s search database via `ReleaseIngest` if measured latency requires them. Protected by a release-independent withdrawal tombstone ledger. | Read-only serving over published releases. Every surface honors monotonic withdrawals and rollbacks. | [Search](../architecture/search.md), [public site](../architecture/public-site.md), `WithdrawalCascade` in [publication](../architecture/publication.md) |

## Stages

| Stage | Plane | What it does |
|---|---|---|
| 1. Records | 1 | Store typed, immutable revisions of statements, bill editions and provisions, votes and casts, sponsorships, actions, executive actions, document changes, and outcome datasets. |
| 2. Deterministic links | 1 | Build verified foreign-key links, vote procedural effects and motion polarities, edition-bound sponsorships, and passage methods (roll call, voice vote, unanimous consent). |
| 3. Candidate connections | 2 | Propose judged links asynchronously in order of trust: explicit identifiers, official citations, lexical text match, then semantic text match. Bound fan-out to top K per source item. Record basis, method version, and `as_of_utc`. |
| 4. Gates | 1, 2, 3 | Code approves Plane 1 facts after citation checks. The solo maintainer approves Plane 2 judgments (accept, reject, or insufficient evidence) within a fixed weekly capacity budget, assisted by deterministic code checks and optional LLM rubric challenges. Plane 3 patterns pass the [pattern gates](#rules-for-patterns). |
| 5. Aggregation | 3 | Compute official, issue, bill-family, committee, chamber, and time-window summaries per release with denominators, legislative stage reached, base rates, and source coverage (R-56). |
| 6. Display | 4 | Render connections with three-layer progressive disclosure: plain-English story and guardrail first, one-step interactive path and source link second, full audit metadata in a collapsed drawer. |
| 7. Corrections and withdrawals | 4 | Enforce `WithdrawalCascade` on active releases and check a release-independent monotonic withdrawal tombstone ledger on every `public_api` view and `ReleaseIngest` read so no rollback or cache warm can resurrect a withdrawn record. |

## Connection model

Each Plane 2 judged connection is a directed, typed relation between two exact record revisions. Under accepted decisions [D-19 and D-20](../evidence-paths.md#pending-decisions), the private pilot extends `RelationshipRevision` without changing `Publication.Contracts` until the pilot passes:

- **Type and direction.** For example `StatementAboutVote`, `DescribesOwnBill`, `StatesPositionOn`, `ClaimsCreditFor`, `ProvisionContinues`, or `AwardFundsProject`. A statement that touches both a bill passage and a roll-call vote is stored as two typed edges sharing one `StatementQuote` span (`Statement -> Action` with passage span, and `Statement -> Vote` with role).
- **Target role and motion polarity.** For a vote: `SpecificMotion`, `WholeBillPackage`, `Procedural`, or `NoIndividualCast`, paired with a deterministic `ProceduralEffect` / `MotionPolarity` (for example pass measure, adopt amendment, table or kill amendment, recommit, invoke cloture, or waive budget rule). For a quotation: `MemberOwnWords`, `OfficeNarrative`, or `QuotedThirdParty`. For a sponsorship: `SponsoredOn` bound to the bill edition current on that date, flagging full substitute replacement ("shell bills").
- **Basis.** `ExplicitBillNumber`, `OfficialCitation`, `LexicalMatch`, or `SemanticMatch`. Relax `RelationshipRevision.ValidateSpans()` so lexical and semantic links do not require a literal bill-number substring inside `StatementQuote`.
- **Evidence.** Both endpoint revisions, `StatementQuote` span, optional `TargetPassageSpan` (precedent: `ActionTextCitation`), `as_of_utc`, retrieval method version, and release number.
- **Decision.** `Approved`, `Rejected` (`NotRelated`, `Duplicate`, or `InsufficientEvidence`), with reviewer identity, timestamp, and `candidate_set_digest` of the exact candidates shown. `RelationshipStaleness` reopens `Rejected` rows only when the latest reason is `InsufficientEvidence` and the input fingerprint or evidence cutoff changes.

Precedents: `src/CivicLens.Core/Relationships/RelationshipRevision.cs`, `src/CivicLens.Core/Actions/ActionTextCitation.cs`, `src/CivicLens.Application/Review/ReviewReasons.cs`, and `src/CivicLens.Infrastructure/Relationships/RelationshipStaleness.cs`.

## Rules for every connection

1. **Show what a connection is, never what it means.** Use one plain-English display vocabulary: *discussed the same issue*, *addressed the same proposal*, *voted on the whole bill*, *voted on the specific motion*, *voted on a procedural step*, and *later outcome observed* ([claims](claims.md#how-to-add-an-issue)). Never show a whole-bill or procedural vote as a position on one provision.
2. **State motion polarity and package context.** Never show a raw Yea or Nay on a procedural motion (table, recommit, cloture, rule, budget waiver) without stating what a Yea vote operationally did. Whenever a statement about one provision sits near a whole-bill vote, state the bill's scope and show any direct amendment vote on that provision beside or above the package vote.
3. **Outcomes have three tiers and require trend plus flow context.** A *measured change* states what the source reported on two dates and that the record does not show why. An *attributed cause* quotes the speaker who credits an actor. An *independent evaluation* cites GAO, an inspector general, or a peer-reviewed study. Civic Lens publishes no causal finding of its own. Any date anchor at a term start or signature date requires at least 12 months of pre-period trend. For stock metrics such as a claims backlog, show flow metrics (inflows and completions) and note definition changes when available.
4. **The speaker is exact.** A quotation from another office's release, an office narrative, or a quoted third party never stands as a member's own position and never receives code approval. Label third-party hosting clearly (for example, "Quoted in Sen. Klobuchar's release").
5. **Bind exact evidence and edition dates.** Each connection binds endpoint revisions, spans, bill edition, `as_of_utc`, and release. Sponsorship links bind `SponsoredOn` to the edition current on that date and flag full-text substitute replacements ("Sponsored an earlier edition; text was later replaced").
6. **Insufficient evidence is a first-class result.** A reviewed absence names the exact missing record (for example, unanimous consent with no individual cast) in plain English. Private candidate lists never reach readers.
7. **One rule for every official and issue, with structural stratification.** Selection of audit cases, examples, and featured items follows a published deterministic rule stratified by branch/chamber, party, and claim/source type so office communication asymmetries do not skew output. Show the frame size, rule, and source coverage meter (R-56) before publishing cross-official comparisons. Replays report balance by party, chamber, and topic.
8. **Every number names who and what it counts and its time frame.** A baseline-dependent figure takes both ends from one series, shows the grid of defensible baselines and end dates in the Audit Drawer, and states whether the result holds across the grid. Agency self-reported measures carry an explicit "Agency-reported" label until an independent lineage exists.
9. **Monotonic withdrawal reaches every surface.** `WithdrawalCascade` blocks withdrawal of cited records in active releases, and a release-independent withdrawal tombstone ledger prevents rollbacks or cache warms from resurrecting withdrawn records. Map, list, timeline, feed, share preview, search, and citation export each have one withdrawal test and a 15-minute target purge bound for the route trial.

## Rules for patterns

A pattern is a Plane 3 aggregate that suggests structure across records. Separate **recurring patterns** from **single-window anomalies** so the statistical gates match the claim:

- **Shared requirements for every pattern:**
  - **Base rate and legislative stage.** Show comparable base rates and distinguish introduced bills from committee-reported, passed, and enacted bills.
  - **Null model.** Compare against a null that preserves party, chamber, committee, and degree. Show the null comparison beside the pattern in the Audit Drawer.
  - **Sensitivity.** Run leave-one-record-out across every contributing record and report the full range, never one chosen removal.
  - **Pre-registered test families for FDR.** Control the false discovery rate (Benjamini-Hochberg) within a pre-registered, bounded test family (for example, all active tracked issues in one chamber-week, never an unbounded cross-product search). Publish the family size.
  - **Minimum counts.** Show no rate, median, or percentile with fewer than 10 events or fewer than 20 items still at risk for time-to-event estimates.
  - **Record drill-down and source coverage.** Every pattern links to its exact underlying records and displays source coverage limits (R-56).
- **Recurring patterns** (such as recurring cosponsor groups or recurring voting splits): must also **replicate in a later time window** that the discovery step did not use. Never use "coalition" or "bloc" until a party-and-committee null and next-window replication pass.
- **Single-window anomalies** (such as unusual legislative activity this week): do not require next-window replication. Instead, compare against a trailing multi-month baseline that explicitly adjusts for congressional recesses, collection backfills, and duplicate procedural motions, and require a verifiable material event behind any alert.

## Evaluation

[Evaluation and observability](evaluation.md) owns the study design. For each Plane 2 connection type, the pipeline enforces these definitions:

- **Case set.** Every frozen case stays in the denominator. Retrieval failures, timeouts, parse errors, and missing bodies count as "no sufficient set" and "no assessment". A case leaves only for a pre-recorded exclusion reason (for example, source withdrawn before cutoff).
- **Candidate coverage.** Answerable cases with a complete, role-valid sufficient evidence set in the first five distinct candidates, divided by all answerable cases.
- **Unsupported attribution rate.** Unsupported positive attributions divided by all positive attributions.
- **Unsupported attribution risk.** Unsupported positive attributions divided by all cases, including insufficient cases. Report alongside coverage so abstention cannot hide errors.
- **Wrong speaker.** Separate error count with 0% tolerance in the pilot.
- **Review throughput.** Median maintainer review minutes per candidate and per accepted finding.
- **Development families.** H.R. 5376 (117th), S. 5 and S. 146 (119th), H.R. 3746 (118th), H.R. 6833, S. 3696, H.R. 2, H.R. 3684, H.R. 9600, and all bills linked to them by official related-bill keys are development data. Every holdout excludes them.
- **Holdout size and draw (D-23).** At least 10 unseen bill families, at most 5 answerable cases per family, about 42 answerable and 30 insufficient cases in total. Draw cases with a seeded deterministic script over a pre-registered stratified frame committed in the pilot PR before running retrieval. Compute intervals with families as clusters.
- **Solo-maintainer label reliability (D-23).** Because Civic Lens has one maintainer and no second human annotator, validate labels with three checks before scoring any method:
  1. Freeze a deterministic checklist rubric (speaker origin, vote role, motion polarity, edition match, insufficient criteria) before opening the holdout.
  2. Perform **blinded intra-rater re-labeling** on at least 20 shuffled holdout cases separated by at least 7 days with prior labels and method outputs hidden; require intra-rater Cohen's kappa of at least 0.80.
  3. Run deterministic code assertions (XML casts, edition dates, speaker origin) plus an **LLM devil's-advocate audit** that flags potential rubric violations for maintainer adjudication (never approving). Disclose single-maintainer adjudication on the public Methods page.
- **Baselines.** Every candidate method faces BM25 with the same bill-vote join, a calibrated BM25 abstention threshold, and the production policy-area gate where applicable, tested against a realistic full-release distractor corpus (not only the 6-bill trial set).

A pilot of about 72 cases can test coverage, abstention (at most 10%), and wrong-speaker failures. It cannot certify a 2% public error target ([D-18](../evidence-paths.md#pending-decisions)); ongoing public release audits handle post-launch certification.

## Before the pilot opens its holdout

Run these zero-cost setup experiments in priority order. Each takes a few hours and unblocks a concrete pilot or production safety decision.

| Priority | Experiment | Question it answers | Cost |
|---|---|---|---|
| 1 | Speaker controls | Does `StatementDraftTask` and code approval prevent attributing guest quotes or office narratives to the host member on multi-speaker releases? Test 20 spans from 6 releases with 2+ speakers and 1 third party. Any wrong-speaker error blocks code approval on multi-speaker releases. | About 4 hours |
| 2 | Frozen release export and input bundle | Can a replay on another day load identical rows and external files? Export one active release via `civic_lens_public` (`REPEATABLE READ`), package pinned external inputs with `SHA256SUMS` outside Git ([D-22](../evidence-paths.md#pending-decisions)), commit one `requirements.lock`, and add `--bundle` support. | About 5 hours |
| 3 | Binding dry run | Which pilot fields (`TargetPassageSpan`, `TargetRole`, `MatchBasis`, `InsufficientEvidence`, `candidate_set_digest`, `SponsoredOn` edition) have no home in `RelationshipRevision` today, and where does `ValidateSpans()` block lexical/semantic matches? Map the 7 trial cases in a unit-test fixture. | About 2 hours |
| 4 | Full-corpus distractor retrieval check | Does BM25 plus explicit bill-vote join still find the right bill and provision when searching all bills in a full published release (thousands of sections) instead of 6 bills and 5 roll calls? | About 4 hours |
| 5 | Vote-role relabel | Does the explicit-link gain survive role-aware scoring (treating package votes offered for provision claims as unsupported)? Relabel the 7 retrieval cases and 2 paired-vote cases. | About 2 hours |
| 6 | Fixture casts and `LATERAL` cast query | Are the 6 hand-typed casts in [the reuse fixture](experiments/reuse-issue-facts.sql) verified against Clerk XML? Also rewrite the timed-out cast query from [published-graph](experiments/published-graph.md) using `LATERAL` and record its `EXPLAIN ANALYZE` plan. | About 2 hours |
| 7 | Policy-area gate baseline | Does the production bill policy-area gate miss secondary omnibus issues more than BM25? Run it on the 7 headings. | About 1 hour |
| 8 | Single-series outcome and pre-trend check | Does the [VA example](experiments/va-backlog.md) hold on one weekly workbook series from Jan 2024 through Nov 2026, and what do claim inflow/completion trends show? | About 3 hours |
| 9 | Pattern replication | How much of a bounded pattern search replicates in the next Congress under a party-preserving null? | About 1 day |

Later, each with its trigger: build the three-state reader mockup and run a 6-person think-aloud ($120) before any Phase 3 UI build; run a 4-arm first-click navigation test (including the shipped DR6 header as control, ~144 participants) only when navigation changes are scheduled; run provision survival (at least 29 dropped/changed transitions) when a change timeline is scheduled; run funded-project delivery (5 recipients with decoys) when a delivery stage is scoped.

Do not run now: more clustering, more paired-vote counts, coalition recurrence, a `release_issues` versioning cost study, a second frontend stack comparison, the two-order record-vs-timeline reader study, or lobbying linkage. No pending decision depends on them.
