"""Cluster bill provisions without using issue, bill or probe labels as features."""
import json
import sys
from collections import Counter, defaultdict
from itertools import combinations
from math import comb
from pathlib import Path
from statistics import mean

import numpy as np
from sklearn.cluster import KMeans
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.metrics import adjusted_rand_score, silhouette_score
from threadpoolctl import threadpool_limits

from bill_xml import read_xml, sections


def corpus(directory, sources):
    bodies, bills, references = [], [], defaultdict(list)
    for bill, source in sorted(sources.items()):
        root = read_xml(directory, bill, source["sha256"])
        for section in sections(root):
            number = section.findtext("enum", "").rstrip(".")
            references[f"{bill}/{number}"].append(len(bodies))
            bills.append(bill)
            bodies.append(" ".join(section.itertext()))
    return bodies, bills, references


def probe_index(references, key):
    # Printed section numbers can recur in different divisions of a bill.
    rows = references.get(key, [])
    if len(rows) != 1:
        raise ValueError(f"Expected one unambiguous section for {key}")
    return rows[0]


def cross_bill_pair_counts(labels, bills):
    joint = Counter(zip(labels, bills))
    same_cluster = sum(comb(n, 2) for n in Counter(labels).values())
    same_bill = sum(comb(n, 2) for n in Counter(bills).values())
    both = sum(comb(n, 2) for n in joint.values())
    return same_cluster - both, comb(len(labels), 2) - same_bill


def main(directory):
    folder = Path(__file__).parent
    previous = json.loads((folder / "issue-cases.json").read_text())
    study = json.loads((folder / "cluster-cases.json").read_text())
    sources = {key: previous["inputs"][key] for key in previous["bills"]}
    sources.update(study["additional_bills"])
    bodies, bills, references = corpus(directory, sources)
    vectorizer = TfidfVectorizer(stop_words="english", min_df=2, max_df=0.95,
                                 sublinear_tf=True, token_pattern=r"(?u)\b[a-zA-Z]{3,}\b")
    features = vectorizer.fit_transform(bodies)
    if np.any(features.getnnz(axis=1) == 0):
        raise ValueError("A section has no usable terms under this vectorizer")
    print("Sections per bill:", dict(Counter(bills)))
    print("Sections and vocabulary:", features.shape)
    runs = {}
    bill_sizes = Counter(bills)
    equal_bill = np.array([1 / bill_sizes[bill] for bill in bills])
    settings = [(8, None), (12, None), (16, None), (12, equal_bill)]
    for k, weights in settings:
        name = f"k={k}" if weights is None else f"k={k} equal-bill"
        fits = []
        for seed in (11, 23, 47):
            model = KMeans(n_clusters=k, n_init=5, random_state=seed, algorithm="lloyd")
            fits.append(model.fit_predict(features, sample_weight=weights))
        runs[name] = fits
        labels = fits[0]
        stability = [adjusted_rand_score(a, b) for a, b in combinations(fits, 2)]
        together, possible = cross_bill_pair_counts(labels, bills)
        print(f"{name}: cosine silhouette {silhouette_score(features, labels, metric='cosine'):.3f}, "
              f"mean seed ARI {mean(stability):.3f}, cross-bill pairs {together}/{possible}, "
              f"cluster size range {min(Counter(labels).values())} to {max(Counter(labels).values())}")

    vocabulary = vectorizer.get_feature_names_out()
    for probe in study["probes"]:
        a = probe_index(references, probe["anchor"])
        b = probe_index(references, probe["comparison"])
        if bills[a] == bills[b]:
            raise ValueError("Each probe must cross bill boundaries")
        counts = [sum(int(labels[a] == labels[b]) for labels in fits) for fits in runs.values()]
        scores = (features @ features[a].T).toarray().ravel()
        # Rank within other bills only. Give tied scores the same best rank.
        rank = 1 + sum(score > scores[b] for i, score in enumerate(scores) if bills[i] != bills[a])
        labels = runs["k=12"][0]
        group = labels == labels[a]
        center = np.asarray(features[group].mean(axis=0)).ravel()
        terms = vocabulary[np.argsort(-center, kind="stable")[:5]]
        print(f"{probe['description']}: together {counts}, cosine {scores[b]:.3f}, "
              f"cross-bill rank {rank}/{sum(bill != bills[a] for bill in bills)}, "
              f"anchor cluster {int(group.sum())} sections, terms {', '.join(terms)}")


if __name__ == "__main__":
    with threadpool_limits(limits=1):
        main(Path(sys.argv[1]))
