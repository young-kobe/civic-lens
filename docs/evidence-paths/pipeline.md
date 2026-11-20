# Relationship pipeline

**Status.** Planned. Nothing on this page is implemented, except the existing owners that it names as precedents.

One pipeline finds connections between records across many dimensions. It aggregates them by official, issue, bill family, committee and time. The public site shows them in maps, lists, timelines and pages. The site shows what the records say and how they connect. It never states a conclusion.

A pipeline that searches many dimensions finds many patterns by chance. It can also turn a true fact into a false impression, for example a vote on a whole bill shown as a position on one provision. The rules below come from the [trial results](experiments/evidence-trials/results.md) and an adversarial review of this plan on 2026-11-20. The review comments are on PR #634. They apply to every connection type, not only to the first pilot.

## Stages

| Stage | What it does | Existing owner or precedent |
|---|---|---|
| 1. Records | Typed, immutable revisions of statements, bill editions and provisions, votes and casts, sponsorships, actions, executive actions and document changes. New sources enter through the collector and their own revision owner. | [Collection](../architecture/collection.md), [publication](../architecture/publication.md), R-58 sources in the [work queue](../evidence-paths.md#accepted-plans) |
| 2. Candidate connections | Propose connections in order of trust: explicit identifiers, official citations, lexical text match, semantic text match, then statistical association. Each candidate records its basis and method version. | Vote-bill link, [relationship drafting](../architecture/statements.md#relationship-drafting) |
| 3. Gates | Code approves a connection that rests on an official identifier and passes its citation checks. A person approves a connection that needs judgment. A statistical pattern passes the [statistical gate](#rules-for-patterns) first. A model prediction is never an approval. | AGENTS.md approval rule, [review](../architecture/review.md) |
| 4. Aggregation | Count and compare approved connections by official, issue, bill family, committee, chamber and time window. Each aggregate keeps its denominator, base rate and coverage. | Boards and release counts in [publication](../architecture/publication.md) |
| 5. Display | Show each connection with its type, role, basis and evidence. Let the reader expand one hop at a time and reach the source in one step. | [Graph prototype](graph.md#a-local-graph-in-relationship-review), [reader phase](reader.md) |
| 6. Corrections | A withdrawal or correction reaches every surface that shows the connection. | `WithdrawalCascade` in [publication](../architecture/publication.md) |

## Connection model

Each connection is a directed, typed relation between two exact record revisions. The schema choice goes to the owner first ([D-19, D-20](../evidence-paths.md#pending-decisions)). The fields to discuss are:

- **Type and direction.** For example "statement about a vote", "provision part of edition", "cast on question", "sponsored edition", "award funds project".
- **Role.** For a vote: the specific motion, the package, a procedural question, or no individual cast. For a quotation: the member's own words, an office narrative, or a quoted third party. For a sponsorship: the edition that was current on the sponsorship date.
- **Basis.** Explicit identifier, official citation, lexical match, semantic match, or statistical association. A reader sees the basis.
- **Evidence.** Both endpoint revisions, the cited spans on both sides, the as-of time, the method version and the release.
- **Decision.** Accept, reject, or insufficient evidence, with the reviewer, the time and a digest of the candidate set that the reviewer saw.

Precedents: `src/CivicLens.Core/Relationships/RelationshipRevision.cs` binds a statement span to one target revision. `src/CivicLens.Core/Actions/ActionTextCitation.cs` binds a span of bill text. `src/CivicLens.Application/Review/ReviewReasons.cs` holds the reject reasons, which today have no "insufficient evidence" code. `src/CivicLens.Infrastructure/Relationships/RelationshipStaleness.cs` never reopens a rejected pair, so an absence verdict needs its own reopen rule.

## Rules for every connection

1. **Show what a connection is, never what it means.** Use one display vocabulary: *discussed the same issue*, *addressed the same proposal*, *voted on the package*, *voted on the specific motion*, and *later outcome observed* ([claims](claims.md#how-to-add-an-issue)). A vote on a whole bill or on a procedural question is never shown as a position on one provision. Each cast near a quotation carries a line that says what the vote can and cannot show.
2. **Outcomes have three tiers.** A *measured change* says what the source reported on two dates and that the record does not show why. An *attributed cause* quotes the speaker who credits an actor. An *independent evaluation* cites GAO, an inspector general or a peer-reviewed study. Civic Lens publishes no causal finding of its own. A date anchor at a term start or a signature date needs the trend for at least the 12 months before it.
3. **The speaker is exact.** A quotation from another office's release, an office narrative and a quoted third party never stand as a member's own position. These never get code approval.
4. **Bind exact evidence.** Each connection binds its endpoint revisions, spans, edition, as-of time and release. A second reviewer can replay it from a frozen input.
5. **Insufficient evidence is a result.** A reviewed absence names the missing evidence. Private candidate lists never reach readers.
6. **One rule for every official and issue.** Selection of cases, examples, featured items and audits follows a published rule, such as a seeded draw from a defined frame. The page shows the frame size and the rule. Replays report balance by party, chamber and topic.
7. **Every number names who and what it counts and its time frame.** A figure that depends on a chosen baseline takes both ends from one series. It shows the grid of defensible baselines and end dates and says whether the result holds across the grid. A measure that comes only from the agency that makes the claim is labeled as agency-reported.
8. **Withdrawal reaches every surface.** Map, list, timeline, feed, share card, search and export each have one withdrawal test. A rollback must not show a record that was withdrawn later. Exported cards carry the release, the export date and a link to check for corrections.

## Rules for patterns

A pattern is an aggregate that suggests a relationship, such as members who often back the same bills, an issue with unusual activity, or an outcome that moved after a decision. A pattern goes to readers only when it passes these checks:

- **Base rate.** Show how often the pattern appears for comparable records. The [coalition trial](experiments/evidence-trials/results.md) shows the risk: one popular bill made most of the repeated pairs.
- **Null model.** Compare with a null that keeps party, chamber, committee and degree. Show the comparison beside the pattern.
- **Sensitivity.** Leave out each contributing record in turn and report the range, not one chosen removal.
- **Replication.** The pattern repeats in a later time window that the search did not use.
- **Multiple tests.** Control the false discovery rate across all patterns that the search tested, not only the ones shown.
- **Minimum counts.** Show no rate, median or percentile with fewer than 10 events.
- **Record drill-down.** Every pattern links to the records that make it.

## Evaluation

[Evaluation and observability](evaluation.md) owns the study design. For each connection type, the pipeline adds these definitions:

- **Case set.** Every frozen case stays in the denominator. Retrieval failures, timeouts and missing bodies count as "no sufficient set". A case leaves only for a reason recorded before scoring.
- **Candidate coverage.** Answerable cases with a complete sufficient evidence set in the first five candidates, over all answerable cases.
- **Unsupported attribution rate.** Unsupported positive attributions over all positive attributions.
- **Unsupported attribution risk.** Unsupported positive attributions over all cases, including insufficient cases. Report it with coverage, so that abstention cannot hide errors.
- **Wrong speaker.** A separate error count.
- **Development families.** H.R. 5376 (117th), S. 5 and S. 146 (119th), H.R. 3746 (118th), H.R. 6833, S. 3696, H.R. 2, H.R. 3684, and bills linked to them by an official related-bill key are development data. A holdout excludes them.
- **Holdout size.** At least 10 unseen bill families, at most 5 answerable cases in each, about 42 answerable and 30 insufficient cases in total. Intervals treat families as clusters.
- **Labels.** Double-label at least 20 cases first. Require Cohen's kappa of at least 0.7 before scoring.
- **Baselines.** Each method faces BM25 with the same bill-vote join, a BM25 score threshold that abstains, and the production policy-area gate where it applies.

A pilot of about 72 cases can test coverage and a 10% abstention target. It cannot certify a 2% public error target ([D-18](../evidence-paths.md#pending-decisions)).

## Before the pilot opens its holdout

These setup experiments cost about $0 and a few hours each. They make the pilot measurable.

| Experiment | Question it answers | Cost |
|---|---|---|
| Input bundle and replay | Do all headline numbers reproduce on a clean machine? Build one bundle of pinned files with `SHA256SUMS`, a lock file for the Python tools, and the expected output of each script. Keep it out of Git ([D-22](../evidence-paths.md#pending-decisions)). | About 3 hours |
| Frozen release export | Can two reviewers on different days load the same rows? Retained releases fall out of the 30-release window in about a day and a half. | About 2 hours |
| Binding dry run | Which pilot fields have no home in `RelationshipRevision`? Map the 7 trial cases onto the existing fields. | About 2 hours |
| Vote-role relabel | Does the explicit-link gain survive role-aware scoring? Relabel the 7 retrieval cases and 2 paired-vote cases. | About 2 hours |
| Speaker controls | Does extraction keep the speaker in releases that quote several people? Use 20 spans from 6 releases. | About 4 hours |
| Fixture casts | Are the 6 typed casts in [the reuse fixture](experiments/reuse-issue-facts.sql) correct? Check them against the Clerk XML. | 30 minutes |
| Policy-area gate baseline | Does the production gate miss secondary issues more than BM25? Run it on the 7 headings. | About 1 hour |
| Single-series outcome check | Does the [VA example](experiments/va-backlog.md) hold on one weekly series, and was the count falling before the baseline? | About 3 hours |
| Pattern replication | How much of what a pattern search finds repeats in the next window? Freeze the top patterns of one Congress and check the next. | About 1 day |

Later, each with its trigger: provision survival (when a change timeline is scheduled), funded-project delivery (when a delivery stage is scoped), a first-click navigation test of about 144 people with the shipped header as the control arm, and a comprehension study of at least 36 readers. A think-aloud with 6 first-time readers on three link states can run before the reader build, at about $120.

Do not run now: more clustering, more paired-vote counts, coalition recurrence, a catalog cost study, a second frontend stack comparison, the current two-order reader study, or lobbying linkage. No pending decision depends on them.
