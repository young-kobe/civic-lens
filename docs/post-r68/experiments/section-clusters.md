# Do clusters of bill sections find related policies across bills?

**Finding.** Clustering found two closely worded Medicare insulin provisions across bills. It also grouped a private-insurance provision with the Medicare text and grouped two severability clauses together. A refueling tax credit and an infrastructure grant landed in different clusters in every standard run. For issue discovery, give reviewers ranked cross-bill neighbors and let them explore clusters. A shared cluster alone gives no evidence about a member's position or a policy outcome.

## Corpus and method

We kept the four editions in the [issue-mapping pilot](issue-mapping.md) and added four bills with provisions on similar subjects. The sample contains 876 outer sections from eight official GovInfo XML editions. The 633 sections of the Infrastructure Investment and Jobs Act and 153 sections of the Inflation Reduction Act account for about 90% of it. The sample deliberately includes overlapping subjects, different legal mechanisms and common drafting language. It is a development corpus.

| Additional edition | Sections | Reason to include it |
|---|---:|---|
| [Affordable Insulin Now Act, House-passed](https://www.govinfo.gov/app/details/BILLS-117hr6833eh) | 5 | Compare closely worded Medicare provisions and a different insurance population. |
| [Infrastructure Investment and Jobs Act, enrolled](https://www.govinfo.gov/app/details/BILLS-117hr3684enr) | 633 | Compare grants for charging and fueling with a tax credit for refueling property. |
| [DEFIANCE Act, Senate-passed](https://www.govinfo.gov/app/details/BILLS-118s3696es) | 4 | Compare a civil remedy for intimate digital forgeries with criminal prohibitions and platform obligations. |
| [Secure the Border Act, House-passed](https://www.govinfo.gov/app/details/BILLS-118hr2eh) | 71 | Compare family detention standards with the detention provision of the Laken Riley Act. |

Each observation is the text of one section, including its heading and any statutory amendments quoted inside it. The shared XML reader keeps those quoted amendments inside their enclosing section. We kept short titles, boilerplate and unnumbered sections. For a named comparison, the runner requires an unambiguous printed section number within the pinned bill edition.

We applied TF-IDF to English alphabetic words of at least three letters with sublinear term frequency and L2 row normalization. We removed English stop words and terms present in fewer than two or more than 95% of sections. The result had 5,222 terms. Numeric amounts and section numbers do not survive this representation. The model receives no issue tags, member identities, roll calls, probe labels or bill IDs as features. Legislative wording can still identify a bill family.

We ran ordinary K-means at 8, 12 and 16 clusters. Each setting used seeds 11, 23 and 47, with five initializations per seed. We chose seven illustrative section pairs from their legislative subject and headings before fitting. As a comparator, we ranked sections in other bills by cosine similarity to each anchor using the same vectors. Ties get the best shared rank. After seeing the imbalance, we repeated the 12-cluster run with each bill carrying equal total weight. Only the fit weights changed. [scikit-learn document clustering example](https://scikit-learn.org/stable/auto_examples/text/plot_document_clustering.html).

## Results

Silhouette measures how well a section fits its group relative to nearby groups using cosine distance. Values near zero indicate weak average separation. Adjusted Rand index (ARI) measures agreement between two partitions after adjusting for chance. The table averages ARI over the three seed pairs. Silhouette, size and pair counts use seed 11 and count each section equally, including for the weighted fit.

| Fit | Cosine silhouette | Mean seed ARI | Smallest to largest group | Cross-bill pairs assigned together out of 169,079 |
|---|---:|---:|---:|---:|
| 8 clusters | 0.030 | 0.410 | 20 to 217 | 14,708 |
| 12 clusters | 0.031 | 0.393 | 19 to 141 | 6,957 |
| 16 clusters | 0.036 | 0.383 | 20 to 118 | 5,379 |
| 12, equal weight per bill | -0.015 | 0.990 | 1 to 837 | 146,319 |

The weighted run illustrates why seed agreement alone is a poor selection criterion. At seed 11 it placed **837 of 876 sections in one cluster**. Repeating an uninformative partition can yield high agreement.

| Selected cross-bill pair | Standard runs together out of 9 | Comparison's cosine rank among the anchor's other-bill sections |
|---|---:|---:|
| Medicare Part D insulin cost sharing in two bills | 9 | 1 of 723 |
| Intimate digital images, criminal prohibition and civil remedy | 8 | 1 of 871 |
| Immigration detention, different populations | 6 | 3 of 873 |
| Refueling infrastructure, property credit and grant | 0 | 1 of 723 |
| Insulin scope control, Medicare and private insurance | 9 | 2 of 723 |
| Drafting control, two severability clauses | 9 | 1 of 871 |
| Numbering control, Medicare and Healthy Streets, both section 11406 | 0 | 249 of 723 |

The 12-cluster Medicare group had 62 sections. Its highest-weight words included *striking* and *inserting*, common instructions for amending a statute. The immigration anchor's group had 70 sections, with *homeland*, *security*, *border*, *alien* and *immigration* as its five highest-weight words. A group can reflect legislative form or issue vocabulary. A human still needs to check the affected population, policy mechanism and exact text.

## Replay

Run from the repository root. Download each of the eight pinned XML inputs once to a temporary directory. The analysis reads the downloaded inputs locally and checks each digest. The measured environment used CPython 3.10.12 on x86-64 Linux. The library pins below match the run. The script limits the numerical libraries to one thread.

```bash
set -e
cluster_inputs=$(mktemp -d)
python3 - "$cluster_inputs" <<'PYTHON'
import json
from pathlib import Path
import subprocess
import sys

folder = Path("docs/post-r68/experiments")
old = json.loads((folder / "issue-cases.json").read_text())
new = json.loads((folder / "cluster-cases.json").read_text())
sources = {key: old["inputs"][key] for key in old["bills"]}
for key, source in (sources | new["additional_bills"]).items():
    path = Path(sys.argv[1]) / f"{key}.xml"
    subprocess.run(["curl", "-fsSL", "--max-time", "30", "-o", path, source["url"]], check=True)
PYTHON
uv run --no-project --python 3.10 \
  --with scikit-learn==1.7.2 --with numpy==2.2.6 --with scipy==1.15.3 \
  --with threadpoolctl==3.6.0 --with joblib==1.5.3 \
  python docs/post-r68/experiments/cluster-sections.py "$cluster_inputs"
```

The [runner](cluster-sections.py) prints all metrics and the cluster settings for each probe. The [probe and source manifest](cluster-cases.json) identifies the exact pair direction and four additional file digests.

## What the run establishes

This test concerns lexical similarity among bill sections. It includes no statements or votes in the fit and supplies no estimate of political alignment. The paired examples are few and deliberately selected. Neighbor rank on those examples does not establish retrieval accuracy across an issue.

For the next comparison, freeze unseen bill families with multiple independently labeled pairs per issue. Include shared topics with different beneficiaries, unrelated topics with similar legislative language, and provisions with no substantive neighbor. Compare TF-IDF neighbors, sentence-embedding neighbors and clustering as a candidate source at the same review budget. Count reviewed relevant cross-bill neighbors, missed links and false attributions by issue before using clusters on a public page.
