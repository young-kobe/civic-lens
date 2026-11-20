# Council role 1: Methods and statistics adversary

Read briefs/council-common.md on this branch first (git show origin/cloud-briefs:briefs/council-common.md). Follow it exactly. This file names your angle.

Role name: Methods and statistics.

Read: docs/post-r68/evaluation.md, docs/post-r68/claims.md, docs/post-r68/graph.md (research sections), docs/post-r68/experiments/evidence-trials/results.md, docs/post-r68/experiments/applied-methods.md, and the experiment notes they cite.

Attack these:
- Sample sizes and confidence. For each rate the plan reports (for example 4 of 4, 9 of 10, 22 of 58 then 1 of 15, 7 of 100, 9 of 95), give an exact binomial 95% interval and say what decision the rate can and cannot support.
- Leakage and reuse. Find cases that served as development examples and also as evidence. Check that the holdout and the "fresh bill family" rule prevent tuning on test cases. Check who chose the cases and whether choice bias favors the method.
- Denominators. Check that abstention, insufficient-evidence cases, and failed retrievals stay in the denominator everywhere. Check that "coverage" and "unjustified attribution" have exact, separate definitions and a fixed cutoff.
- Error tolerance. The README says the owner sets error tolerance and minimum coverage before tuning. Propose concrete starting values with reasons, based on the harm of a false attribution to a named official versus a missed link. Give the sample size needed to show the error rate is below that value with 95% confidence.
- Baselines. Check that each method faces a simple baseline (lexical search, exact citation match, sponsor lookup) under the same budget. Flag any comparison without one.
- Clustering and coalition results. Check the stability claims (nine standard runs, silhouette, adjusted Rand). Say whether the sponsor-overlap result needs a null model, for example a party-preserving or degree-preserving shuffle.
- VA backlog. Check the cutoff and baseline choices for forking paths. Say how the plan should report a figure that depends on a chosen baseline.
- Reader study design. Check whether the planned comprehension test can tell the two layouts apart. Give a minimum reader count.

Experiment advice is your main output. For each README "next decision" row, say Run now, Run later, or Do not run, with the design and sample size.
