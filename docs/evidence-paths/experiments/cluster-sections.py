"""Cluster bill provisions without using issue, bill or probe labels as features."""
import argparse
import json
from collections import Counter, defaultdict
from itertools import combinations
from math import comb
from pathlib import Path
from statistics import mean

import numpy as np
from scipy.cluster.hierarchy import cut_tree, linkage
from scipy.spatial.distance import squareform
from sklearn.cluster import KMeans
from sklearn.feature_extraction.text import ENGLISH_STOP_WORDS, TfidfVectorizer
from sklearn.metrics import adjusted_rand_score, pairwise_distances, silhouette_score
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


def vectorize(bodies, stop_words="english"):
    vectorizer = TfidfVectorizer(stop_words=stop_words, min_df=2, max_df=0.95,
                                 sublinear_tf=True, token_pattern=r"(?u)\b[a-zA-Z]{3,}\b")
    features = vectorizer.fit_transform(bodies)
    if np.any(features.getnnz(axis=1) == 0):
        raise ValueError("A section has no usable terms under this vectorizer")
    return vectorizer, features


def hierarchy_trial(bodies, bills, references, probes):
    # The full cosine distance matrix grows quadratically. Keep this a bounded replay.
    if len(bodies) > 2000:
        raise ValueError("The hierarchy trial supports at most 2000 sections")
    drafting = {"amended", "amendment", "clause", "inserting", "paragraph",
                "section", "striking", "subparagraph", "subsection"}
    largest = Counter(bills).most_common(1)[0][0]
    groups = (("full", list(range(len(bills))), "english"),
              ("omit drafting terms", list(range(len(bills))), sorted(ENGLISH_STOP_WORDS | drafting)),
              ("omit largest bill", [i for i, bill in enumerate(bills) if bill != largest], "english"))
    base_labels = None
    for name, rows, stops in groups:
        _, features = vectorize([bodies[i] for i in rows], stops)
        dist = pairwise_distances(features, metric="cosine")
        np.fill_diagonal(dist, 0.0)
        tree = linkage(squareform(dist, checks=True), method="average")
        k_values = [8, 12, 16]
        labels = cut_tree(tree, n_clusters=k_values)
        local_bills = np.array([bills[i] for i in rows])
        local_index = {original: i for i, original in enumerate(rows)}
        print(name, "sections", len(rows), "bills", len(set(local_bills)))
        for k, partition in zip(k_values, labels.T):
            print("groups", k, "largest", max(Counter(partition).values()),
                  "cross-bill pairs", cross_bill_pair_counts(partition, local_bills))
        if base_labels is None:
            base_labels = labels[:, 1]
        else:
            print("ARI versus full on shared rows at 12 groups",
                  round(adjusted_rand_score(base_labels[rows], labels[:, 1]), 3))
        for probe in probes:
            a = local_index.get(probe_index(references, probe["anchor"]))
            b = local_index.get(probe_index(references, probe["comparison"]))
            if a is None or b is None:
                print(probe["description"], "absent")
                continue
            others = local_bills != local_bills[a]
            rank = 1 + np.count_nonzero(others & (dist[a] < dist[a, b]))
            print(probe["description"], "together", (labels[a] == labels[b]).tolist(),
                  "cross-bill rank", rank)


def main(directory, hierarchy=False):
    folder = Path(__file__).parent
    previous = json.loads((folder / "issue-cases.json").read_text())
    study = json.loads((folder / "cluster-cases.json").read_text())
    sources = {key: previous["inputs"][key] for key in previous["bills"]}
    sources.update(study["additional_bills"])
    bodies, bills, references = corpus(directory, sources)
    if hierarchy:
        hierarchy_trial(bodies, bills, references, study["probes"])
        return
    vectorizer, features = vectorize(bodies)
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
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--hierarchy", action="store_true")
    args = parser.parse_args()
    with threadpool_limits(limits=1):
        main(args.directory, args.hierarchy)
