# What a sponsorship graph adds

**Question.** Can a reader trace a path between two pilot bills using published facts? How much extra structure does a graph algorithm create from the same facts?

## Sample and method

On 2026-11-18 at 22:52:42 UTC, a read-only transaction on the Orin read active release **1416** through the published-view role. The release contained 19,649 action records, 1,597 votes, 694 executive actions, and 33 statements. It contained zero relationship records. These are publication counts. They reveal little about material awaiting collection, drafting, or review.

We chose H.R. 2102 and H.R. 9600 in the 119th Congress because they appeared on the same homepage and motivated different reader questions. The sample is purposive. The SQL joined active sponsorships to the exact published bill revisions and votes by the published bill link. It found common backers by official ID. A local NetworkX graph represented each bill revision and official as a node. Each active sponsor or cosponsor supplied one typed edge. NetworkX independently computed the common neighbors from those same rows. Its weighted projection connected each pair of officials with one weight per shared pilot bill.

| Bill revision | Active backers | Linked roll call observed | Evidence |
|---|---:|---|---|
| H.R. 2102, revision 5 | 336 | 323, passage, Nov 12, 392 Yes and 18 No, revision 3 | [Bill](https://civic-lens.info/records/255f3cff527e84846dc610a7ffd6412a74539de9e20f1add8f93a9d2ac920ff3.html) and [vote](https://civic-lens.info/records/106a58cb391836965ee444d606bee2619be9881876e146da3556696f1728b2db.html) |
| H.R. 9600, revision 2 | 2 | 325, suspension, Nov 17, 365 Yes and 36 No, revision 1 | [Bill](https://civic-lens.info/records/f81eb3a35e81e55d64e827b392d37dab6351e30bbda02f8a2031f1d9fbe718e5.html) and [vote](https://civic-lens.info/records/e8e1d2f25200157330d663eab65e2dbaff4219ca5ef6dcfaa82386c5f64e9161.html) |

## Result and interpretation

- SQL and NetworkX returned the same sole common backer, Jamie Raskin. This is a reproducible two-hop path through sponsorship. The two methods used the same source, so their agreement tests the computation only.
- There were **337 distinct officials, 2 bills, and 338 sponsorship edges**. Projecting officials onto officials produced **56,281 distinct pairs**, each sharing exactly one pilot bill. H.R. 2102 alone contributed 336 × 335 / 2 = 56,280 pairs. H.R. 9600 contributed one. A network picture would look dense even though every derived connection has only one observation behind it.
- A reader can ask which bill gives rise to an edge and inspect its source. A coalition ranking from these two bills would mostly measure the popularity of H.R. 2102. Keep bill identity, denominator, date and sponsorship role available for drill-down. Active sponsorship describes the snapshot. An as-of-vote analysis would also need the dates of sponsorship and withdrawal.
- Federal Register documents 2026-23348 and 2026-18738 each had a published revision 1, one executive action source row and zero summary-source rows. Inspection found Federal Register API metadata in those two source rows. Full-text claim review needs a captured body. This snapshot cannot establish which bodies the operational collector holds.
- An exploratory join from the two votes to member casts stopped at a four-second timeout. The completed experiment measures sponsorship and vote totals only. It supplies no engine speed comparison or estimate for member votes at scale.

## Reproduce

To replay release 1416 offline, use the [frozen input](published-1416.json). This small fixture contains the published identities and revisions needed to verify the paths, plus the SQL comparison result. It omits document bodies and member casts.

```sh
uv run --no-project --with networkx==3.4.2 python \
  docs/post-r68/experiments/project-sponsors.py < docs/post-r68/experiments/published-1416.json
```

To sample the current release, use PostgreSQL 18 and a connection that can read the published views. Run from the repository root. The query reads one repeatable-read snapshot with a five-second statement timeout and a 500 ms lock timeout. The analysis reads the export locally. The measured environment used Python 3.10.12 and NetworkX 3.4.2.

```sh
psql -X -qAt -v ON_ERROR_STOP=1 --dbname=service=civic_published \
  -f docs/post-r68/experiments/published-graph.sql > /tmp/civic-published-graph.json
uv run --no-project --with networkx==3.4.2 python \
  docs/post-r68/experiments/project-sponsors.py < /tmp/civic-published-graph.json
```

Substitute a configured published-view connection for the example service. On the Orin we used a local database socket and the `civic_lens_public` role. Keep subsequent exports outside Git. The scripts reject a missing pilot bill, a repeated sponsorship edge, an unidentified official or an overlap mismatch. The local projection caps the number of officials at 600.

The views follow the active release. A later run samples that release and prints its number, revisions and UTC timestamp. The frozen input preserves the JSON values from the measured query with whitespace changes only. The reported counts also permit an independent arithmetic check. The live page URLs can also advance. [SQL sample](published-graph.sql) and [NetworkX analysis](project-sponsors.py).
