"""Compare published bill sponsorship overlap in PostgreSQL and NetworkX."""
import json
import sys
from collections import Counter

import networkx as nx
from networkx.algorithms import bipartite


def main():
    snapshot = json.load(sys.stdin)
    bills = {b["bill_number"]: ("bill", b["record_id"], b["revision_number"])
            for b in snapshot["bills"]}
    if set(bills) != {2102, 9600} or len(snapshot["bills"]) != 2:
        raise ValueError("Both published pilot bills must have exactly one revision")
    graph = nx.Graph()
    graph.add_nodes_from(bills.values(), bipartite=0)
    for row in snapshot["sponsorships"]:
        bill = bills[row["bill_number"]]
        official = ("official", row["official_id"])
        if not row["official_id"] or graph.has_edge(bill, official):
            raise ValueError("Expected one active sponsorship per bill and identified official")
        graph.add_node(official, bipartite=1)
        graph.add_edge(bill, official, role=row["role"])
    shared = sorted(node[1] for node in nx.common_neighbors(graph, bills[2102], bills[9600]))
    if shared != snapshot["sql_shared"]:
        raise ValueError("SQL and NetworkX disagree on the identities of the shared backers")
    officials = [node for node, side in graph.nodes(data="bipartite") if side == 1]
    if len(officials) > 600:
        raise ValueError("The sample exceeds the local projection budget")
    projection = bipartite.weighted_projected_graph(graph, officials)
    weights = Counter(data["weight"] for _, _, data in projection.edges(data=True))
    print("Release:", snapshot["release"])
    print("Active backers by bill:", {number: graph.degree(node) for number, node in bills.items()})
    print("Distinct officials:", len(officials))
    print("Sponsorship edges:", graph.number_of_edges())
    print("Shared backers:", shared)
    print("Official pairs by number of shared pilot bills:", dict(sorted(weights.items())))
    for field in ("as_of_utc", "record_counts", "bills", "votes", "executive_documents"):
        print(field, json.dumps(snapshot[field], sort_keys=True))


if __name__ == "__main__":
    main()
