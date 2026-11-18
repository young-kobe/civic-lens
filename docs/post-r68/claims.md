# Phase 1. Claims and evidence

## Two reader questions

**What did House roll call 325 decide?** In release 1416 the vote page identified suspension procedure, a two-thirds threshold, and the tally. The purpose of H.R. 9600 was to approve a location for a Thomas Paine memorial. Reps. Raskin and Spartz discussed the measure in a release on November 17. Join the applicable bill text, House decision and dated statement. Attribute each quote to its speaker. Distinguish House approval from enactment and a completed memorial. The external statement is a candidate source. The [published sample](experiments/published-graph.md) found no published relationship records. [Bill](https://www.govinfo.gov/app/details/BILLS-119hr9600rh), [roll call](https://clerk.house.gov/Votes/2026325?Page=2) and [sponsors' statement](https://raskin.house.gov/2026/11/raskin-spartz-legislation-finalizing-details-for-national-memorial-honoring-indispensable-american-patriot-thomas-paine-passes-house).

**Which veterans-benefits claims can a reader check?** The proclamation mentions a fiscal count, a decline, an executive action and a housing goal. Its gratitude to veterans also supplies a useful example of a passage to skip. The two pilot executive records exposed metadata sources in our sample. Capture and validate the official body before extracting a publishable claim. Use R-61 for executive source coverage. [Proclamation](https://www.govinfo.gov/content/pkg/FR-2026-11-16/pdf/2026-23348.pdf) and [executive action pipeline](../architecture/executive-actions.md).

| Proposition | Evidence to seek | Public result it could justify |
|---|---|---|
| VA processed a record 3.1 million disability claims this year | Fiscal period, population and comparable historical counts. VA reports 3,111,399 disability compensation claims in FY 2026. | Agreement with an attributed agency report. Independent confirmation needs a separate source lineage. [VA report](https://news.va.gov/press-room/va-in-fy-2026-processes-the-most-claims-ever/). |
| The backlog declined by more than 70% during the term | A named baseline, cutoff, consistent measure and exact cells. | A dated calculation with sensitivity to endpoints. [Worked example](experiments/va-backlog.md). |
| A September order accelerated benefits access | Separate the signature and directed tasks from completion and service outcomes. | The order documents the directive. Track the 120-day contract task and look for subsequent evidence of completion and timeliness. [Executive order](https://www.whitehouse.gov/presidential-actions/2026/09/accelerating-access-to-veterans-benefits-and-employment-opportunities/). |
| The administration aims to house up to 6,000 homeless veterans by 2028 | Preserve the qualifier, target population, due date and unit. Look for progress reports. | A commitment with a deadline. People housed, available beds and appropriated funds measure different things. |

The House passed the Major Richard Star Act on November 12. The proclamation was signed on November 10. The vote fits the issue timeline. Its existence cannot verify an outcome that the president described as already achieved.

## Proposed workflow and ownership

1. The collector fetches bounded official material under existing source rules. Separate campaign remarks, official actions, agency measurements and quoted third parties by origin.
2. Application proposes atomic claims from the saved body. Classify factual actions, numbers or comparisons, outcome or causal claims, positions and future commitments. Preserve the original passage, speaker, qualifiers, units, population and time window. Split a statement that claims both an action and its effect.
3. Retrieve candidates using explicit identifiers, official names, dates and program text. Seek contrary evidence and independent source families. An LLM may propose questions and supporting passages. Code validates spans and computes defined quantities. A missing body, ambiguous speaker or missing baseline becomes a visible gap.
4. Use the existing review queue and exact-version decision pattern. A reviewer decides disputed meaning, attribution and the interpretation of outcomes. Publication includes only eligible reviewed revisions and cited evidence. The public API exposes published contracts. [Relationship drafting and review](../architecture/statements.md#relationship-drafting), [analysis](../architecture/analysis.md) and [publication](../architecture/publication.md).

## Proposed relational design

| Unit | Identity and meaning |
|---|---|
| Claim and claim revision | Stable claim identity and an immutable revision. Bind the source record kind, record revision, exact extraction and span. Store the normalized proposition separately from the quote. Structured values can be unknown. |
| Evidence link | A directed relation between a claim revision and a typed evidence version. Cite a span, official fact or a dataset cell with its source digest. Keep the source family, event date, observation date, and evidence availability date when known. |
| Assessment revision | Bind one claim revision and the exact selected links. Record question, as-of cutoff, method version, calculation, contrary evidence, reviewer and limitations. A new finding adds a revision. |
| Published insight | Select the eligible assessment and evidence revisions in one release. If a dependency changes, flag the finding for review. A withdrawal invalidates affected public routes, features and caches. Retain correction history. |

These are responsibilities for schema discussion. Use typed foreign keys and existing revision owners where possible. Decide how to bind a spreadsheet region and how claim identity survives splits or merges before migration. Queue work with its trigger in the same transaction. Replaying the same extraction and task version should preserve identity. Exercise duplicate imports, stale review, withdrawal, and a failed run that retries. The approval of an exact quote establishes what was said. An assessment of its accuracy needs its own evidence.

The first assessment labels to test with reviewers are supported for the specified scope, conflicts with the specified evidence, mixed, insufficient evidence, and target pending. Collect human disagreement before choosing public wording. AVeriTeC offers a model for checking an answer through intermediate evidence questions and temporal cutoffs. [AVeriTeC study](https://papers.neurips.cc/paper_files/paper/2023/hash/cd86a30526cd1aff61d6f89f107634e4-Abstract-Datasets_and_Benchmarks.html).
