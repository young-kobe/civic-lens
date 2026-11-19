# Phase 1 onward. Evaluation and observability

**First deliverable.** Given a weak public answer, an authorized reviewer can trace it to an extraction, retrieval, calculation or interpretation error. Given a proposed change, the owner can compare the same cases, see regressions and decide whether the gain justifies the cost.

## What to measure

Use the COSC 4397 Project 1 rubric's definitions for Format Compliance Rate, Object Position Accuracy and Exact Match Rate. The course task orders five objects. Document each Civic Lens adaptation, its population and its denominator.

| Measure | Definition for the pilot | Example use |
|---|---|---|
| FCR | Structurally compliant outputs divided by all tested outputs. Report the first attempt and the result after allowed repairs. | A structured claim has all required fields, valid types and identifiers. Assess the accuracy of their values separately. |
| OPA | Objects in the correct absolute position divided by the number of expected positions across cases. Score identifiable positions independently of formatting. | Fixed-length event sequences with unambiguous gold order. Exclude ties or define their acceptable order before testing. |
| EMR | Cases with compliant format and all required values and positions correct divided by all cases. Specify allowed normalization up front. | Canonical IDs, ordered events, a bounded structured extraction. A paraphrase of an explanation needs semantic assessment. |
| Claim extraction | Precision and recall against human-identified propositions. Report exact span validity and speaker, negation, qualifier, unit and cutoff errors separately. | Include ceremonial text and skipped sources. Otherwise missed claims disappear from the denominator. |
| Retrieval | Fraction of answerable cases with a sufficient evidence set in the first five results. Also count retrieval of relevant counterevidence, wrong entities and evidence unavailable at the cutoff. | Ten copies of an agency press release have one origin. A citation can resolve and still fail to support the proposition. |
| Assessment | Per-label precision and recall, confusion matrix, and fraction of supported calls a reviewer finds unsupported. Report abstentions, coverage and human disagreement. | Inspect false support and false contradiction individually. Assess accuracy on answered questions alongside the share answered. |
| Explanation | Supported atomic assertions divided by all checkable assertions. Record omitted qualifications and incorrect citations independently. | A summary can reproduce a number and still omit its fiscal year. [FActScore](https://aclanthology.org/2023.emnlp-main.741/). |
| Operation | Runs, queue delay, p95 completion time, retries, budget waits, tokens, cost per reviewer-accepted finding, review minutes and corrections after publication. | A cheaper prompt may shift effort into manual review. Record the price schedule, sample size and cache use. |

## Study design

Start with the [multi-issue development cases](experiments/issue-mapping.md) and the [numeric outcome audit](experiments/va-backlog.md) to write a labeling guide. Build a separate evaluation set from representative documents, including non-claims, missing evidence, stale revisions, agency repetition, opposition quotes and counterexamples. Keep document families and near-duplicates together. Reserve a later time window and unseen families for the holdout. Freeze the evidence available at the specified cutoff. Label the expected source spans, acceptable evidence sets and reasons to abstain. Have two people independently label a subset and adjudicate disagreement. Record sample sizes by source, claim type, party when attributable, agency and date. Show uncertainty intervals for rare errors. Two successful examples cannot establish a population error rate.

The [first food-assistance transfer trial](experiments/evidence-trials/results.md) used two answerable questions from one bill family. Treat those cases as development data. Reserve another unseen issue family for testing issue transfer. Collect new statements, eligibility provisions and recorded decisions from different bill families. Keep the model, prompt and retrieval settings fixed. Report results for that issue separately. For a genuinely new catalog entry, ask a reviewer to add only a definition and examples, then test untouched cases before tuning. Record any issue-specific code that would have been necessary. This distinguishes an extensible workflow from a good fit to a few examples.

For each run save the dataset and rubric versions, model identifier, task, prompt, retrieval and schema versions, available source revisions, raw denominators, cost and errors. Pair old and new outputs by case. Add exact and cheap rules as baselines. Include negative controls, such as a correct number from the wrong fiscal year, a missing speaker, and a stale assessment. A model judge can triage cases after calibration against blinded human labels. A reviewer owns interpreted public findings. Report provision-level multi-label precision and recall per issue, issue-macro averages, and unsupported member-position attributions separately. Test a new issue with frozen rules and model settings. Keep every edition of a bill in one partition, even if it spans multiple issues.

Before tuning, the owner chooses the tolerable false-support rate and minimum useful coverage. Choose sample size from those targets. Fail the release trial on a fabricated citation, a withdrawn dependency, or a future source used as contemporary evidence. Where sample size cannot distinguish the candidates, report the uncertainty and keep the stronger existing baseline. Paid comparisons remain opt-in under R-67 budgets. Routine CI can replay fixed public fixtures and exercise the scorers without provider calls.

## Instrumentation and tool trial

Existing runs already bind exact inputs, versions, token use and the draft revision. The statement gate has a held-out human labeling path. Extend these owners. Add correlated stage timing and error categories for retrieval, generation, validation, review and publication. Put run and trace IDs on private drill-downs. Keep high-cardinality case IDs out of metric labels and restrict prompt contents and unpublished material to the existing authorized store. The public methods page can show dated aggregate results, coverage, denominators and limitations. [Analysis runs](../architecture/analysis.md) and [statement labeling](../architecture/statements.md).

| Candidate | Trial and tradeoff |
|---|---|
| OpenTelemetry and C# scorers | Instrument one task at the adapter boundary. Check trace completeness through one retry. Retain portable counters and joins to the authoritative run. [GenAI semantic conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/). |
| Microsoft.Extensions.AI.Evaluation | Run a labeled set offline and compare its reporting with the current prompt-measure workflow. Keep evaluation packages outside production domain projects. [Evaluation libraries](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries). |
| Phoenix or Langfuse | Compare one candidate on replay, dataset versioning, annotations, retention, .NET integration effort and memory. Start with one local experiment. Adopt a service if it reduces the effort of diagnosing regressions. [Phoenix experiments](https://arize.com/docs/phoenix/datasets-and-experiments/overview-datasets) and [Langfuse experiments](https://langfuse.com/docs/evaluation/experiments/experiments-via-opentelemetry). |

The graph and backlog experiments supply methods and seed cases. Neither ran a claim classifier or measured model accuracy. A first private scorecard should expose that distinction.
