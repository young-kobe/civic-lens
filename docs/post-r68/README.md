# Post-R-68 plan appendix

Civic Lens should let a citizen follow a public claim through a decision to a measured outcome. Start with a question that matters to the reader. Give each answer a date, an accountable actor, and a short path to the original evidence.

The R-68 agent owns [closeout of the deployed redesign](../roadmap.md#active-r-68-redesign), including recovery, rollout and verification. The fresh agent can run the private evidence pilot independently on frozen data. The remaining phases are proposed build-out. Existing [roadmap items](../roadmap.md#accepted-plans) retain their dependencies. The [architecture reference](../architecture.md) describes shipped behavior. Move accepted behavior into its feature reference as each phase ships.

| Phase | Deliverable | Evidence for moving forward |
|---|---|---|
| 1. [Claims and evidence](claims.md) | A reproducible outcome audit and a multi-issue test of statement, provision and vote links | A reviewer can reconstruct each finding from exact source versions and explain the remaining gaps. |
| 1, from the first case. [Evaluation and observability](evaluation.md) | A private run comparison, a labeled holdout and a public methods summary | Show which stage improved, the effect on critical errors and coverage, and the cost per accepted finding. |
| 2. [Graph and statistical research](graph.md) | Typed paths across records and a bounded comparison of retrieval methods | Each additional correct link survives review and leads to cited evidence. Measure the cost of maintaining the projection. |
| 3. [Reader experience](reader.md) | Issue timelines, a changing homepage and a collapsible left navigation | New readers can explain what happened, find their member's part, and distinguish a verified fact from an open question. |
| 4. [Frontend delivery](delivery.md) | One independently deployable issue route using the published API | A shell survives an unavailable API. Deep links, withdrawals, privacy and release consistency pass the route trial. |

The [evidence interface](evidence-interface.md) shows which reader questions can use existing joins and which need additional classified evidence. The next phase will keep JSON authoring, store each public issue definition once per version and bind each release to its membership, definition versions and counts. The private pilot can use the current published views before that migration.

## First assignment for the next phase

Start a fresh branch from current main. Read the [claims workflow](claims.md#proposed-workflow-and-ownership), [relationship review](../architecture/statements.md#relationship-drafting), [evaluation plan](evaluation.md) and [trial results](experiments/evidence-trials/results.md). Use a frozen published release and pinned source documents in development.

Build one private review flow. The [local graph prototype and reader counterpart](graph.md#a-local-graph-in-relationship-review) demonstrate the same sources in map and list views. Put the attributed quotation and its date beside the exact bill edition and passage, the roll-call question, and the member's recorded cast when one exists. Let the reviewer accept or reject the proposed link or record insufficient evidence. Bind that decision to the evidence revisions so a second reviewer can reproduce it. Start with the existing relationship-drafting and review handlers. Bring any required schema or public-contract choice to the owner using those handlers as the precedent.

Demonstrate the flow on a supported link and a tempting wrong link, then test on a separate bill family. Capture complete evidence sets in the first five candidates, unsupported attributions and review time when a reviewer is available. Keep cases without a sufficient vote in the denominator for evaluating abstention. The two food-assistance positives in the trial share a bill and provision. They are now development examples. Reserve fresh cases for the next transfer test.

Hand back the private workflow in its PR with a replay command, pinned inputs, baseline and candidate counts, and one decision a reviewer can reconstruct. List unresolved cases and the missing evidence for each. The pilot can finish with an inconclusive result. The product owner uses the [evaluation criteria](evaluation.md#pilot-decision-and-feedback) to choose whether to retain the baseline, fund a specific follow-up or scope a public trial. A later, separately scoped [public issue-route trial](delivery.md#one-route-before-a-stack-decision) can use a reviewed finding to test a direct link, API unavailability and evidence withdrawal before choosing a frontend delivery approach. The reader phase owns [navigation and homepage selection](reader.md).

## Experiments

| Experiment | Result and repeatable materials |
|---|---|
| [AI and statistics on existing evidence](experiments/applied-methods.md) | Tested agreement between two retrievers, hierarchical clustering of 876 bill sections, and time to a committee report for 64 bills. The [research priorities](graph.md#which-ai-or-statistical-study-comes-next) tie each next comparison to a product decision. |
| [Versioned issue definitions](evidence-interface.md#can-a-versioned-table-replace-the-repeated-definitions) | Twelve retained releases contained 192 issue rows and 16 distinct public definitions. A separate synthetic rollback check preserved historical wording, counts and membership. |
| [Reuse of issue assignments across features](evidence-interface.md#offline-interface-check) | Three House votes supported two reader views. Adding a poll supplied two contextual matches and zero eligible directional comparisons. [Runnable SQL](experiments/reuse-issue-facts.sql). |
| [Five linked-evidence trials](experiments/evidence-trials/results.md) | Tested quote retrieval, provision continuity, charging delivery and coalition recurrence. Exercised all reader form assignments at two widths. Human comprehension remains unmeasured. |
| [Published graph on two House bills](experiments/published-graph.md) | In release 1416, 338 sponsorships produced 56,281 member pairs. The same common backer appeared in SQL and NetworkX. [Query](experiments/published-graph.sql) and [analysis](experiments/project-sponsors.py). |
| [Clustering bill provisions across bills](experiments/section-clusters.md) | Clustered 876 sections in eight editions. Closely worded insulin provisions grouped together in all nine standard runs. A related refueling grant and credit stayed apart. |
| [Amendment or motion versus final passage](experiments/paired-votes.md) | Of senators casting Yea or Nay on both questions, 7 of 100 cast different votes in the insulin case and 9 of 95 in the immigration case. |
| [Issue mapping across legislative subjects](experiments/issue-mapping.md) | Tested statement-to-provision retrieval and topic candidates on four bill editions. Verified four roll-call questions and seven selected casts. |
| [Veterans claims backlog at two cutoffs](experiments/va-backlog.md) | VA's inauguration-day count gives a 72.07% decline by Oct 10 and 69.95% by Nov 7. Two weekly baselines also cross the threshold. [Workbook calculation](experiments/va-backlog.py). |

## Which research would change the next build?

The [five completed trials](experiments/evidence-trials/results.md) establish the starting point. Only the first row belongs to the private pilot. Reopen the other questions when the named input is available and the result would change a product decision.

| Question | What the completed trial established | Next decision and evidence needed |
|---|---|---|
| Can a reviewer connect a quote to the right decision? | Explicit links recovered 4 of 4 selected provision-and-vote pairs in the first five. Every method returned candidates for all 3 cases with insufficient evidence. | Compare against lexical search on an untouched bill family, with the same five slots and cutoff. Score candidate coverage and unjustified attributions separately. The owner chooses a method after seeing both. |
| Did a provision survive into the next edition? | Text matching got 9 of 10 selected transitions right. One false match changed the legal effect. | Before offering a change timeline, test fresh editions with a changed mechanism or a dropped provision. Keep uncertain transitions open for review. |
| Did a funded project reach delivery? | The Deerfield case linked an obligation and a reported opening. The opening report omitted the award ID. | Test a second recipient with a dated site-level completion or expenditure record. Until then, show each verified stage and its source. |
| Do shared sponsors form a recurring coalition? | Removing the most widely backed early bill reduced later overlap from 22 of 58 repeated pairs to 1 of 15. | Revisit a coalition feature with wider time windows and a comparison accounting for party or committee. Keep the underlying bill links available. |
| Do readers understand the evidence? | The two-layout instrument ran at both widths. No first-time readers took part. | When readers are available, score their explanations and source use. Test the navigation structure separately by asking readers to find a destination. [Reader trial](reader.md#decision-test). |

## Decisions to take in order

1. Start the private quote-to-vote review. Use the existing revision and review owners. Before tuning, the product owner sets error tolerance and minimum coverage, and the pilot agent freezes the comparison. The catalog migration can proceed separately after its release-equivalence checks are ready. [Pilot evaluation](evaluation.md#pilot-decision-and-feedback) and [catalog migration](evidence-interface.md#can-a-versioned-table-replace-the-repeated-definitions).
2. Use PostgreSQL joins for known paths. Commission a graph or model comparison when a named reader question and a measured weakness justify it. Keep the simpler method if results are inconclusive. [Graph research](graph.md).
3. Use first-time readers to choose the navigation and test comprehension. Use published releases to replay featured-item selection. Each study answers a different question. [Reader decisions](reader.md#decision-test).
4. Scope one public issue route around an eligible reviewed finding. Choose its runtime after testing API outages, revision consistency, withdrawals, accessibility and rollback. The owner decides wider rollout after seeing the comparison with the deployed route. [Delivery trial](delivery.md#acceptance-trial).
