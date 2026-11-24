# Phase 1 onward. Evaluation and observability

**First deliverable.** Given a weak public answer, the maintainer can trace it to an extraction, retrieval, calculation, or interpretation error. Given a proposed change, the maintainer can compare the exact same frozen cases, see regressions, and decide whether the gain justifies the cost and review time.

## What to measure

Keep Format Compliance Rate (`FCR`) for structured outputs. Drop COSC 4397 `OPA` and `EMR` until a real task requires scoring ordered positions. Document each Civic Lens metric, its population, and its denominator explicitly.

| Measure | Definition for the pilot | Example use |
|---|---|---|
| FCR | Structurally compliant outputs divided by all tested outputs. Report the first attempt and the result after allowed repairs. | A structured claim or link has all required fields, valid types, valid role enum, and valid identifiers. Assess accuracy separately. |
| Claim extraction | Precision and recall against rubric-labeled propositions. Report exact span validity and speaker, negation, qualifier, unit, and cutoff errors separately. | Include ceremonial text, multi-speaker releases, and skipped sources so missed claims stay in the denominator. |
| Candidate coverage | Answerable cases with a complete, role-valid sufficient evidence set in the first five distinct candidates, divided by all answerable cases. | Every frozen case stays in the denominator. Timeouts, fetch failures, and missing bodies count as no sufficient set. Also count retrieval of counterevidence, wrong entities, and after-cutoff records. |
| Unsupported attribution rate | Unsupported positive attributions divided by all positive attributions. | Measures precision of positive links. Undefined when positive attributions equal zero. |
| Unsupported attribution risk | Unsupported positive attributions divided by all frozen cases, including insufficient-labeled cases. | Report beside coverage in a risk-coverage pair so higher abstention cannot disguise error rates. |
| Wrong speaker | Cases where a quote is attributed to the wrong person, or an office narrative or third-party quote is attributed as a member's own position. | Tracked as a separate P1 error count with 0% tolerance in the pilot. |
| Abstention on insufficient cases | Insufficient-labeled cases where the method still asserts a position, divided by all insufficient-labeled cases. | Target upper bound at most 10% in the pilot. |
| Explanation | Supported atomic assertions divided by all checkable assertions. Record omitted qualifications, missing motion polarity, and incorrect citations independently. | A summary can reproduce a number and still omit its fiscal year or pre-period trend. [FActScore](https://aclanthology.org/2023.emnlp-main.741/). |
| Operation and review throughput | Runs, queue delay, p95 completion time, retries, budget waits, tokens, cost per accepted finding, maintainer review minutes per candidate and per accepted link, and post-publication corrections. | Because Civic Lens has one maintainer, a method that raises recall by flooding the review queue fails operationally. Record price schedule, sample size, and cache use. |

## Study design for a solo maintainer

Civic Lens has a single maintainer and no second human annotator. Build the study so that single-maintainer evaluation stays rigorous, reproducible, and auditable rather than depending on unavailable human reviewers:

1. **Freeze the development registry.** The nine prior trial bill families (H.R. 5376, S. 5, S. 146, H.R. 3746, H.R. 6833, S. 3696, H.R. 2, H.R. 3684, H.R. 9600) plus all bills linked to them by official related-bill keys are development data. Every holdout excludes them.
2. **Pre-register the sampling frame and draw seed (D-23).** Commit a deterministic seeded sampling script in the pilot PR *before* running retrieval or viewing drawn rows. Stratify the frame by branch/chamber, party, and claim/source type over a fixed date window after method freeze. Size the holdout to at least 10 unseen bill families, at most 5 answerable cases per family, about 42 answerable cases and 30 insufficient cases (~72 total). Keep every edition of a bill in one partition.
3. **Pre-register a checklist rubric.** Before inspecting holdout retrieval results, freeze concrete binary rules for: speaker origin (`MemberOwnWords`, `OfficeNarrative`, `QuotedThirdParty`), vote role (`SpecificMotion`, `WholeBillPackage`, `Procedural`, `NoIndividualCast`), motion polarity, bill-edition match, and insufficient-evidence triggers.
4. **Verify labels without a second human annotator.**
   - Run **deterministic code assertions** first: verify exact XML casts, exact bill-edition dates, exact quote offsets, and `as_of_utc` availability.
   - Perform **blinded intra-rater re-labeling** on at least 20 shuffled holdout cases separated by at least 7 days, hiding prior labels and method outputs. Require intra-rater Cohen's kappa of at least 0.80 on "sufficient vs. insufficient" and "role-valid support" before scoring any method. If kappa falls below 0.80, tighten the rubric and repeat.
   - Run an **LLM devil's-advocate audit** over the labeled set to flag potential rubric violations, alternative readings, or missed counterevidence for the maintainer to adjudicate. The model never approves a label; it challenges blind spots.
   - Disclose single-maintainer adjudication, the published rubric, and intra-rater kappa on the public Methods page, and treat reader reports as the external post-publication audit loop.
5. **Test against realistic baselines and corpus scale.** Run every candidate against BM25 with the same bill-vote join, a calibrated BM25 score-threshold abstention baseline (tuned only on a separate calibration set of ~20 development cases), and the production policy-area gate where applicable. Retrieve over a realistic full-release distractor corpus, not a 6-bill toy index.

For each run save the dataset and rubric versions, model identifier, task, prompt, retrieval and schema versions, available source revisions, raw denominators, cluster-bootstrap uncertainty intervals (families as clusters), cost, review minutes, and errors. Include negative controls: correct number from the wrong fiscal year, package vote offered for a provision quote, inverted procedural motion (table/waiver), multi-speaker wrong-speaker quote, and stale edition sponsorship. Paid comparisons remain opt-in under R-67 budgets. Routine CI replays fixed public fixtures and exercises the scorers without provider calls.

## Pilot decision and feedback

Under accepted decision [D-18](../evidence-paths.md#pending-decisions), the pilot targets are fixed before tuning:

| Target | Accepted value | Notes |
|---|---|---|
| Maintainer-accepted unsupported attribution (private pilot) | Upper bound at most 5% | Evaluated on accepted positive attributions. |
| Maintainer-accepted unsupported attribution (public trial) | Upper bound at most 2% | A 72-case pilot cannot certify 2%; ongoing public release audits certify post-launch safety. |
| Wrong-speaker error (private pilot) | 0% tolerance | Any wrong-speaker failure blocks promotion and code approval on multi-speaker releases. |
| Position asserted on insufficient cases (pilot) | Upper bound at most 10% | At most 3 of 30 insufficient cases (exact upper bound requires 0 in 29 for 10%). |
| Candidate coverage, first five | Observed at least 70%, lower bound above 50% | Evaluated over all frozen answerable cases. |
| Paired gain over lexical baseline | At least 25 points (McNemar) | Smaller gains do not justify extra complexity or review cost. |

Score retrieval and final assessment separately while keeping every frozen case in the denominator. Zero positive attributions leaves unsupported attribution rate undefined; always print numerators and denominators alongside risk-coverage pairs. Retain both the shown candidate list digest and the maintainer decision so a retrieval miss and a judgment error can be diagnosed separately.

Fail any release trial on a fabricated citation, a wrong-speaker attribution, a withdrawn dependency, or a future source used as contemporary evidence. If a candidate beats the baseline by the agreed margin within the error and review-minute budgets, propose the cheaper qualifying method for the next step. If both methods miss a target, keep the work private and fix the failing stage. If the comparison is inconclusive, retain the simpler lexical baseline. Cases used to diagnose or tune move into the development registry; reserve fresh families for any follow-up comparison.

For a later public finding, a reader report, a corrected source, or a withdrawal starts a new review. Trace the affected finding to its input revisions, record the disposition, and verify the corrected or withdrawn result across every public surface (protected by `WithdrawalCascade` and the monotonic withdrawal tombstone ledger). Add the smallest reproducible failure to development evaluation and close the report through the existing review workflow. [Reader reports](../architecture/review.md#reader-reports) and [corrections and withdrawal](../architecture/publication.md#corrections-and-withdrawal).

## Instrumentation and tool trial

Existing runs already bind exact inputs, versions, token use, and the draft revision. The statement gate has a held-out human labeling path. Extend these owners. Add correlated stage timing, review-minute tracking, and error categories for retrieval, generation, validation, review, and publication. Put run and trace IDs on private drill-downs. Keep high-cardinality case IDs out of metric labels and restrict prompt contents and unpublished material to the existing authorized store. The public methods page shows dated aggregate results, coverage, denominators, intra-rater kappa, single-maintainer disclosure, and limitations. [Analysis runs](../architecture/analysis.md) and [statement labeling](../architecture/statements.md).

| Candidate | Trial and tradeoff |
|---|---|
| OpenTelemetry and C# scorers | Instrument one task at the adapter boundary. Check trace completeness through one retry. Retain portable counters and joins to the authoritative run. [GenAI semantic conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/). |
| Microsoft.Extensions.AI.Evaluation | Run a labeled set offline and compare its reporting with the current prompt-measure workflow. Keep evaluation packages outside production domain projects. [Evaluation libraries](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries). |
| Phoenix or Langfuse | Compare only if solo-maintainer regression diagnosis becomes painful; do not add a persistent evaluation service before the pilot finishes. [Phoenix experiments](https://arize.com/docs/phoenix/datasets-and-experiments/overview-datasets) and [Langfuse experiments](https://langfuse.com/docs/evaluation/experiments/experiments-via-opentelemetry). |

The graph and backlog experiments supply seed cases and baseline code. Neither ran a claim classifier or measured full-corpus retrieval accuracy. The first private scorecard must expose that distinction.
