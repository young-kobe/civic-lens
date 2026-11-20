"""Test shared sponsorship against degree-preserving swaps and a later bill sample."""
import random
import re
import sys
from collections import Counter
from itertools import combinations
from pathlib import Path
from statistics import median
from xml.etree import ElementTree as ET

from inputs import verified


def read_bills(directory):
    rosters = []
    for name in ["rosterfeb.xml", "rosterjune.xml"]:
        root = ET.fromstring(verified(directory, name))
        rosters.append({e.get("name-id") for e in root.findall(".//recorded-vote/legislator")})
    eligible = rosters[0] & rosters[1]
    windows = []
    family_keys = set()
    for first in [1001, 4001]:
        bills = {}
        current_keys = set()
        for n in range(first, first + 32):
            bill = ET.fromstring(verified(directory, f"status{n}.xml")).find("bill")
            assert bill.findtext("congress") == "118" and bill.findtext("number") == str(n)
            keys = {("HR", str(n)), ("title", re.sub(r"\W+", " ", bill.findtext("title").lower()).strip())}
            keys.update((r.findtext("type"), r.findtext("number")) for r in bill.findall("relatedBills/item")
                        if r.findtext("congress") == "118")
            if first == 4001 and keys & family_keys:
                continue
            current_keys.update(keys)
            people = {p.findtext("bioguideId") for p in bill.findall("sponsors/item")}
            people.update(p.findtext("bioguideId") for p in bill.findall("cosponsors/item")
                          if p.findtext("isOriginalCosponsor") == "True"
                          and p.findtext("sponsorshipDate") == bill.findtext("introducedDate"))
            bills[n] = people & eligible
        windows.append(bills)
        family_keys.update(current_keys)
    return windows, len(eligible)


def shared(bills):
    return Counter(pair for members in bills.values() for pair in combinations(sorted(members), 2))


def null_counts(bills, future):
    original = [(b, m) for b in sorted(bills) for m in sorted(bills[b])]
    original_degrees = Counter(m for _, m in original)
    counts, recurrences = [], []
    for seed in range(99):
        rng = random.Random(seed)
        edges, sets = list(original), {b: set(ms) for b, ms in bills.items()}
        swaps = 0
        for _ in range(len(edges) * 200):
            i, j = rng.sample(range(len(edges)), 2)
            a, x = edges[i]
            b, y = edges[j]
            if a == b or x == y or y in sets[a] or x in sets[b]:
                continue
            sets[a].remove(x)
            sets[a].add(y)
            sets[b].remove(y)
            sets[b].add(x)
            edges[i], edges[j] = (a, y), (b, x)
            swaps += 1
            if swaps == len(edges) * 10:
                break
        if swaps != len(edges) * 10 or any(len(sets[b]) != len(bills[b]) for b in bills):
            raise ValueError("Incomplete randomization")
        assert original_degrees == Counter(m for members in sets.values() for m in members)
        repeated = [pair for pair, n in shared(sets).items() if n >= 2]
        counts.append(len(repeated))
        recurrences.append(sum(future[pair] > 0 for pair in repeated))
    return counts, recurrences


if __name__ == "__main__":
    (early, late), eligible = read_bills(Path(sys.argv[1]))
    popular = max(early, key=lambda b: (len(early[b]), -b))
    print("Eligible IDs, later bills and edges:", eligible, len(late), sum(map(len, late.values())))
    print("Largest early bill and degree:", popular, len(early[popular]))
    future = shared(late)
    for name, sample in [("full", early), ("omit largest", {b:s for b,s in early.items() if b != popular})]:
        counts, recurrences = null_counts(sample, future)
        repeated = {pair for pair, n in shared(sample).items() if n >= 2}
        print(name, "bills, edges, repeated, null min/median/max, null at least observed, later recurrence:",
              len(sample), sum(map(len, sample.values())), len(repeated), (min(counts), median(counts), max(counts)),
              sum(n >= len(repeated) for n in counts), sum(future[pair] > 0 for pair in repeated))
        print("Later recurrence under null min/median/max:", min(recurrences), median(recurrences), max(recurrences))
