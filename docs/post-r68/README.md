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

Build one private review flow. Put the attributed quotation and its date beside the exact bill edition and passage, the roll-call question, and the member's recorded cast when one exists. Let the reviewer accept or reject the proposed link or record insufficient evidence. Bind that decision to the evidence revisions so a second reviewer can reproduce it. Start with the existing relationship-drafting and review handlers. Bring any required schema or public-contract choice to the owner using those handlers as the precedent.

Demonstrate the flow on a supported link and a tempting wrong link, then test on a separate bill family. Capture complete evidence sets in the first five candidates, unsupported attributions and review time when a reviewer is available. Keep cases without a sufficient vote in the denominator for evaluating abstention. The two food-assistance positives in the trial share a bill and provision. They are now development examples. Reserve fresh cases for the next transfer test.

Hand back the private workflow, its evaluation results and any unresolved evidence cases. A later, separately scoped [public issue-route trial](delivery.md#one-route-before-a-stack-decision) can use a reviewed finding to test a direct link, API unavailability and evidence withdrawal before choosing a frontend delivery approach. The reader phase owns [navigation and homepage selection](reader.md).

## Experiments

| Experiment | Result and repeatable materials |
|---|---|
| [Versioned issue definitions](evidence-interface.md#can-a-versioned-table-replace-the-repeated-definitions) | Twelve retained releases contained 192 issue rows and 16 distinct public definitions. A separate synthetic rollback check preserved historical wording, counts and membership. |
| [Reuse of issue assignments across features](evidence-interface.md#offline-interface-check) | Three House votes supported two reader views. Adding a poll supplied two contextual matches and zero eligible directional comparisons. [Runnable SQL](experiments/reuse-issue-facts.sql). |
| [Five linked-evidence trials](experiments/evidence-trials/results.md) | Tested quote retrieval, provision continuity, charging delivery and coalition recurrence. Exercised all reader form assignments at two widths. Human comprehension remains unmeasured. |
| [Published graph on two House bills](experiments/published-graph.md) | In release 1416, 338 sponsorships produced 56,281 member pairs. The same common backer appeared in SQL and NetworkX. [Query](experiments/published-graph.sql) and [analysis](experiments/project-sponsors.py). |
| [Clustering bill provisions across bills](experiments/section-clusters.md) | Clustered 876 sections in eight editions. Closely worded insulin provisions grouped together in all nine standard runs. A related refueling grant and credit stayed apart. |
| [Amendment or motion versus final passage](experiments/paired-votes.md) | Of senators casting Yea or Nay on both questions, 7 of 100 cast different votes in the insulin case and 9 of 95 in the immigration case. |
| [Issue mapping across legislative subjects](experiments/issue-mapping.md) | Tested statement-to-provision retrieval and topic candidates on four bill editions. Verified four roll-call questions and seven selected casts. |
| [Veterans claims backlog at two cutoffs](experiments/va-backlog.md) | VA's inauguration-day count gives a 72.07% decline by Oct 10 and 69.95% by Nov 7. Two weekly baselines also cross the threshold. [Workbook calculation](experiments/va-backlog.py). |

## Which research would change the next build?

| Priority and reader question | Smallest informative comparison | Decision it informs |
|---|---|---|
| 1. Can we link a real statement to what actually came to a vote? | Freeze original quotes, their speakers, provision versions, roll-call questions and as-of dates. Compare text search, semantic search and text search plus explicit amendment and vote links. Give each method five evidence slots. Blind reviewers to the method and reserve a bill family outside the completed trials. | Start with the least costly method that improves complete evidence sets at five on unseen families while meeting a false-attribution limit chosen before testing. Show unknowns where the source is silent. [Evaluation design](evaluation.md) and [paired roll calls](experiments/paired-votes.md). |
| 2. What changed between proposal and passage? | Trace a small set of provisions across introduced, chamber-passed and enrolled editions. Include renumbering, moved text, dropped text and a nearby provision with similar wording. Compare exact section numbers and text matching against analyst-labeled passages. Count correct continuations, missed removals and false carryovers. | Decide when the timeline can say a provision changed or disappeared. A missing edition gives an unknown result. [Official bill versions](https://www.govinfo.gov/help/bills). |
| 3. Did an announced benefit reach anyone? | Trace one charging grant from statutory authority to obligation and, where documented, an operating site. Measure the share of links with primary-source identifiers and the time needed to verify each. | Decide how far to take a local delivery tracker and which stages need additional sources. [Proposed charging case](claims.md#from-a-law-to-a-local-result). |
| 4. Does the graph uncover more than popular bills? | For one chamber and Congress, compare observed shared sponsorship with randomized simple bipartite graphs preserving each member's activity and each bill's sponsor count. Keep eligibility windows comparable. Remove the most widely backed bill and repeat. Check candidate pairs against later, separate bill families. | Build a coalition discovery view only if the additional pattern recurs and can be explained through specific bills. [Graph pilot](experiments/published-graph.md). |
| 5. Does an evidence timeline teach readers what happened? | Counterbalance a record page and an evidence timeline across comparable tasks. Include a motion with a different subsequent cast and an award with an unknown opening date. Score whether readers can explain the two stages, cite their evidence and recognize the unknown. | Choose the layout using comprehension, time and confidence. Test featured-item selection separately on the same layout. [Reader trial](reader.md#decision-test). |

## Decisions to take in order

1. Choose the first claim types, evidence states, and review rule using the multi-issue cases and the veterans outcome audit. Approve schema and public wording separately. Start the held-out set before prompt tuning.
2. Measure the gain from typed traversal over exact joins and text search. The working recommendation is PostgreSQL for evidence and an offline graph projection for research.
3. Compare branch-first navigation with the proposed reader-first hierarchy on the same tasks. Retain real bill and vote permalinks in both.
4. Choose the frontend runtime after the one-route trial. Keep the public contract independent of which option wins.

The first useful release can be small. A reader should be able to follow the Paine vote to the bill's purpose and the sponsors' own words. The veterans page should let the reader reproduce one number and identify one unanswered question. Run the same workflow on unseen bills and an unseen issue family before extending public coverage.
