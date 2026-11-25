# Three experiments using existing Civic Lens research inputs

These offline runs reused pinned public documents from the provision and vote trials. Each tests a decision a reader or reviewer would encounter. All results describe the selected inputs.

## Does agreement between retrievers identify sufficient evidence?

For each of the seven existing quotes or questions, take the highest-ranked eligible provision from keyword search (BM25) and from a frozen MiniLM embedding search. Compare three ways to send the existing five-slot evidence list to a reviewer. Apply each rule before comparing the list with the reference answers. Bill agreement uses Congress, type and number. Exact agreement also requires the same edition and section. Retrieval uses legislative event dates through the end of the quoted day. The historical availability of the source bytes is unknown. [Cases, sources and original retrieval method](evidence-trials/results.md#1-from-a-quotation-to-a-decision).

| Rule for sending a list | Answerable cases sent with the complete evidence set | Insufficient cases sent | Total lists sent |
|---|---:|---:|---:|
| Always send | 4/4 | 3/3 | 7/7 |
| Both methods select the same bill | 2/4 | 1/3 | 3/7 |
| Both select the same edition and section | 2/4 | 0/3 | 2/7 |

The stricter rule kept the platform deadline and one food-assistance exemption. It dropped a correctly retrieved immigration amendment and the food-assistance sunset. In one case about victims' experience, both methods selected different sections of the same unrelated immigration bill. Agreement can help prioritize review. Requiring agreement to see a candidate would have hidden useful evidence. These seven known cases include two positives from one bill family. No human verdict or calibrated error rate was measured. Next, compare a sufficiency rule and a reranker on untouched families at the same review budget.

## Can hierarchical clustering find a policy in another bill?

Fit average-linkage clustering to cosine distances between the same TF-IDF section vectors used in the [K-means baseline](section-clusters.md). Cut the tree at 8, 12 and 16 groups. Refit after removing nine common legislative drafting terms, then separately after removing the 633-section infrastructure bill. Each comparison rebuilds the vocabulary. Rank the seven existing probes against sections in other bills using cosine distance, giving tied candidates the best shared rank. The study uses fixed development probes and limits the distance matrix to 2,000 sections. [Average linkage](https://docs.scipy.org/doc/scipy/reference/generated/scipy.cluster.hierarchy.linkage.html).

| At 12 groups | Largest group | Cross-bill pairs grouped together | Adjusted Rand agreement with full hierarchy on shared rows |
|---|---:|---:|---:|
| Full hierarchy | 815/876 | 144,228/169,079 | Reference |
| Remove drafting terms | 786/876 | 135,554/169,079 | 0.763 |
| Remove the largest bill | 91/243 | 1,637/15,260 | 0.127 |
| Earlier unweighted K-means, seed 11 | 141/876 | 6,957/169,079 | Unmeasured |

The full hierarchy placed the endpoints of all seven pairs together at every cut, including the Medicare insulin and Healthy Streets sections which both carry the number 11406. For the same insulin anchor, direct cross-bill ranking placed the related Medicare provision first and Healthy Streets 249th. The credit and grant for refueling infrastructure were direct neighbors despite differing legal mechanisms. Removing the biggest bill changed the grouping even among the 243 surviving sections. This sensitivity test measures corpus dependence. It does not estimate performance on unseen bills. For a reviewer, start with ranked neighbors and the exact text. Test semantic reranking with independently labeled mechanisms and populations before offering issue recommendations.

## When did a bill receive a committee report?

We reused the 64 pinned House bill-status histories for H.R. 1001 through 1032 and H.R. 4001 through 4032 in the 118th Congress. The two introduction dates were February 14 and June 12, 2023. The unit is one bill, so the study includes related bills. The event is the first Library of Congress action with source code 9 and action code 5000, reported to House. Each of the three observed events also had a House H12200 entry and corresponding report text on the same date. Deduplicate by bill. A referral or an order to report can happen earlier. This is a retrospective action-date replay from later files. [GPO action code guide](https://github.com/usgpo/bill-status/blob/main/BILLSTATUS-XML_User_User-Guide.md) and [pinned file manifest](evidence-trials/sources.json).

| Cutoff and horizon since introduction | Observed reports by horizon | Bills with unresolved status at horizon | Possible count among the 64 | Kaplan-Meier estimated share reported |
|---|---:|---:|---:|---:|
| Sept 30, 2023, at 30 days | 1 | 0 | 1 | 1.56% |
| Sept 30, 2023, at 180 days | 2 | 31 | 2 to 33 | 3.125% |
| Sept 30, 2023, at 365 days | 2 | 62 | 2 to 64 | Unavailable |
| Dec 31, 2024, at 30 days | 1 | 0 | 1 | 1.56% |
| Dec 31, 2024, at 180 days | 2 | 0 | 2 | 3.125% |
| Dec 31, 2024, at 365 days | 3 | 0 | 3 | 4.69% |

At the September cutoff, the younger bills had under 180 days of observation. Count a bill as unresolved when it has no observed report and its follow-up ends before the horizon. The possible range allows every unresolved bill to have or miss the event. The Kaplan-Meier estimate uses the bills still at risk and assumes censoring is uninformative about subsequent reporting times. The two introduction windows can differ, and this sample is too small to test that assumption well. Withhold an estimate beyond observed follow-up when outcomes remain open. [SciPy right-censored empirical distribution](https://docs.scipy.org/doc/scipy/reference/generated/scipy.stats.ecdf.html).

H.R. 4004, 1023 and 4016 reached the coded event after 9, 37 and 305 days respectively. With all bills observed for a year, only 3 of 64 reached it within 365 days. Showing the median of just those three waits would describe successful cases only. Before publishing comparisons, check status coverage on a wider set of introduction dates, audit uncoded equivalent events, and handle alternate routes such as House passage without a committee report. This sample cannot rank officials or predict passage.

## Replay

Use the eight verified XML files and numerical library versions from the [clustering replay](section-clusters.md#replay). For the other two experiments, use the existing [evidence-trials inputs and environment](evidence-trials/results.md#replay), which include the pinned model and bill-status XML. Run from the repository root with those environments active. The timing runner needs SciPy 1.15.3. The agreement runner also needs pdftotext and the pinned model runtime.

```bash
python docs/connected-records/experiments/cluster-sections.py "$cluster_inputs" --hierarchy
python docs/connected-records/experiments/evidence-trials/text-trials.py "$inputs" agreement
python docs/connected-records/experiments/evidence-trials/report-timing.py "$inputs"
```
