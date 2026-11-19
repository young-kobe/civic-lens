"""Replay a retrospective retrieval baseline on digest-pinned public documents."""
import hashlib
import json
import re
import sqlite3
import sys
from pathlib import Path

from bill_xml import read_xml, sections


def index_sections(db, texts, bill_ids):
    db.execute("CREATE VIRTUAL TABLE provisions USING fts5(key UNINDEXED, body, tokenize='porter')")
    headings = {}
    for bill_id in bill_ids:
        for section in sections(texts[bill_id]):
            number = section.findtext("enum", "").strip().rstrip(".")
            key = f"{bill_id}/{number}"
            if not number or key in headings:
                raise ValueError(f"Missing or duplicate section number in {bill_id}")
            heading = section.find("header")
            headings[key] = " ".join(heading.itertext()) if heading is not None else ""
            body = " ".join(section.itertext())
            db.execute("INSERT INTO provisions VALUES (?, ?)", (key, body))
    return headings


def search_terms(text):
    return " OR ".join(f'"{term}"' for term in re.findall(r"[a-z0-9]+", text.lower()))


def evaluate_topics(db, catalog_path, study, headings):
    content = catalog_path.read_bytes()
    if hashlib.sha256(content).hexdigest() != study["catalog_sha256"]:
        raise ValueError("Issue catalog differs from the study version")
    db.execute("CREATE VIRTUAL TABLE topics USING fts5(id UNINDEXED, cues, tokenize='porter')")
    for issue in json.loads(content)["issues"]:
        cues = " ".join([issue["name"], issue["definition"], *issue["includes"]])
        db.execute("INSERT INTO topics VALUES (?, ?)", (issue["id"], cues))
    complete = labeled = no_topic_hits = unknown = 0
    for key, labels in study["section_labels"].items():
        query = search_terms(headings[key])
        rows = db.execute("""SELECT id FROM topics WHERE topics MATCH ?
                             ORDER BY bm25(topics), id LIMIT 3""", (query,))
        candidates = [topic for topic, in rows]
        print(f"{key}: expected {labels}, candidate topics {candidates}")
        if labels:
            labeled += 1
            complete += set(labels).issubset(candidates)
        else:
            unknown += 1
            no_topic_hits += bool(candidates)
    print(f"All expected topics in top three: {complete}/{labeled}")
    print(f"Unmapped controls with topic candidates: {no_topic_hits}/{unknown}")


def evaluate(db, cases, keys):
    positives = complete = negative_candidates = 0
    for case in cases:
        # All question words enter the same lexical ranker. Labels are scoring data.
        query = search_terms(case["question"])
        rows = db.execute("""SELECT key FROM provisions WHERE provisions MATCH ?
                             ORDER BY bm25(provisions), key LIMIT 3""", (query,))
        result = [key for key, in rows]
        expected = {f"{case['bill']}/{n}" for n in case["sections"]}
        if not expected.issubset(keys):
            raise ValueError(f"Missing expected section for {case['name']}")
        if expected:
            positives += 1
            complete += expected.issubset(result)
            status = f"{len(expected.intersection(result))}/{len(expected)} required sections"
        else:
            negative_candidates += bool(result)
            status = "no sufficient provision in corpus"
        print(f"{case['name']}: {status}; top three: {', '.join(result)}")
    print(f"Complete evidence sections in top three: {complete}/{positives}")
    print(f"Unanswerable cases with candidates: {negative_candidates}/{len(cases) - positives}")


def check_roll_calls(texts, votes):
    for name, expected in votes.items():
        xml = texts[name]
        if xml.tag == "roll_call_vote":
            question = xml.findtext("vote_question_text")
            members = [(m.findtext("lis_member_id"), m.findtext("vote_cast"))
                       for m in xml.findall("members/member")]
        else:
            question = xml.findtext("vote-metadata/vote-question")
            members = [(m.find("legislator").get("name-id"), m.findtext("vote"))
                       for m in xml.findall("vote-data/recorded-vote")]
        if question != expected["question"]:
            raise ValueError(f"Vote question mismatch for {name}")
        for member, cast in expected["members"].items():
            if [value for identifier, value in members if identifier == member] != [cast]:
                raise ValueError(f"Member cast mismatch for {name}, {member}")
        print(f"{name}: checked {question} and {len(expected['members'])} member casts")


def main(directory):
    study = json.loads(Path(__file__).with_name("issue-cases.json").read_text())
    texts = {name: read_xml(directory, name, source["sha256"])
             for name, source in study["inputs"].items()}
    db = sqlite3.connect(":memory:")
    try:
        headings = index_sections(db, texts, study["bills"])
        print(f"{len(headings)} sections in {len(study['bills'])} bill versions; SQLite {sqlite3.sqlite_version}")
        catalog = Path(__file__).resolve().parents[3] / "config" / "issues.json"
        evaluate_topics(db, catalog, study, headings)
        evaluate(db, study["cases"], headings.keys())
    finally:
        db.close()
    check_roll_calls(texts, study["votes"])


if __name__ == "__main__":
    main(Path(sys.argv[1]))
