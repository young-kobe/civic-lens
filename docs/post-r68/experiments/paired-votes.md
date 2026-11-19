# What does a bill's final vote hide about an earlier decision?

A reader following an insulin or immigration statement can encounter several roll calls on the same bill. We compared each senator's recorded cast on an issue-specific question with their cast on final Senate passage. We chose two cases already discussed in the [issue-mapping pilot](issue-mapping.md). Each has an identifiable earlier question and a recorded passage vote. This is a test of what a bill-level join loses in those cases.

## Results

| Senate questions, in order | Yea both | Nay both | Yea then Nay | Nay then Yea | Other rows | Different among Yea/Nay on both |
|---|---:|---:|---:|---:|---|---:|
| H.R. 5376, budget waiver concerning insulin, then final passage on August 7, 2022 | 50 | 43 | 7 | 0 | 0 | 7/100 |
| S. 5, Cornyn amendment on January 15, then final passage on January 20, 2025 | 61 | 25 | 9 | 0 | 3 Not Voting then Yea, 1 Not Voting then Nay | 9/95 |

The insulin waiver needed three fifths and failed 57 to 43. The passage vote split 50 to 50 among senators. The vice president broke the tie. The study denominator covers senators only. [Waiver](https://www.senate.gov/legislative/LIS/roll_call_votes/vote1172/vote_117_2_00314.htm) and [passage](https://www.senate.gov/legislative/LIS/roll_call_votes/vote1172/vote_117_2_00325.htm).

The Cornyn amendment to the Ernst amendment proposed expanding the list of offenses subject to mandatory detention. It passed 70 to 25 with four senators recorded as Not Voting. The amended bill passed 64 to 35. All 99 senators in these two rosters had a row on both calls. [Amendment](https://www.senate.gov/legislative/LIS/roll_call_votes/vote1191/vote_119_1_00003.htm) and [passage](https://www.senate.gov/legislative/LIS/roll_call_votes/vote1191/vote_119_1_00007.htm).

Even when the two casts match, they answer different questions. Show the member's two dated actions and the question for each. To attribute a view about the underlying policy or explain why the member voted that way, find a passage in the member's own words. These deliberately selected pairs give no estimate of how often casts differ across Congress.

## Replay

Run from the repository root with Bash, curl and Python 3. The runner verifies the four official XML digests and reconciles member rows to the official Yea, Nay, Present and absent counts. It checks the session, Congress and amendment's referenced bill against the passage document. It joins the union of Senate LIS member identifiers. A missing row has its own category. The denominator requires a Yea or Nay on each question.

```bash
set -e
vote_inputs=$(mktemp -d)
senate_rolls=https://www.senate.gov/legislative/LIS/roll_call_votes
for number in 314 325; do
  curl -fsSL --max-time 30 "$senate_rolls/vote1172/vote_117_2_00${number}.xml" \
    -o "$vote_inputs/senate-$number.xml"
done
for number in 3 7; do
  curl -fsSL --max-time 30 "$senate_rolls/vote1191/vote_119_1_0000${number}.xml" \
    -o "$vote_inputs/senate-$number.xml"
done
python3 docs/post-r68/experiments/compare-senate-votes.py "$vote_inputs"
```

The [runner](compare-senate-votes.py) reuses three digests from the [issue study](issue-cases.json) and pins the additional amendment roll call. Scope interpretation comes from the official questions. The calculation uses no stance classifier.
