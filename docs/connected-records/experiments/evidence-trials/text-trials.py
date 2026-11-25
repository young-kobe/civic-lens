"""Score the fixed retrieval and provision-continuity reference cases."""
from collections import Counter
from html.parser import HTMLParser
from pathlib import Path
import re
import sqlite3
import subprocess
import sys
from xml.etree import ElementTree as ET

import numpy as np
import onnxruntime as ort
from sklearn.feature_extraction.text import TfidfVectorizer
from tokenizers import Tokenizer

from inputs import CASES, verified

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from bill_xml import sections


def provision_texts(directory, edition):
    root = ET.fromstring(verified(directory, edition + ".xml"))
    texts = {}
    outer = list(sections(root))
    numbers = [s.findtext("enum", "").strip().rstrip(".") for s in outer]
    counts = Counter(numbers)
    for index, (section, number) in enumerate(zip(outer, numbers), 1):
        if not number or counts[number] > 1:
            number = f"{number}@{index}"
        texts[number] = " ".join(" ".join(section.itertext()).split())
    return texts


def vote_document(directory, key):
    root = ET.fromstring(verified(directory, key + ".xml"))
    if root.tag == "roll_call_vote":
        congress = root.findtext("congress")
        measure = root.findtext("amendment/amendment_to_document_number") or root.findtext("document/document_name")
        fields = ["vote_question_text", "vote_document_text", "vote_result_text"]
    else:
        root = root.find("vote-metadata")
        congress, measure = root.findtext("congress"), root.findtext("legis-num")
        fields = ["legis-num", "vote-question", "vote-desc", "vote-result"]
    bill = congress + re.sub(r"[^a-z0-9]", "", measure.lower())
    return bill, " ".join(root.findtext(field) or "" for field in fields)


class PageText(HTMLParser):
    def __init__(self):
        super().__init__()
        self.parts = []

    def handle_data(self, data):
        self.parts.append(data)


def check_quotes(directory):
    originals = {}
    for _, _, key, _, quote, _ in CASES["retrieval"]:
        if key not in originals:
            data = verified(directory, key)
            if key.endswith(".pdf"):
                text = subprocess.run(["pdftotext", "-layout", "-", "-"], input=data,
                                      capture_output=True, check=True).stdout.decode()
            else:
                page = PageText()
                page.feed(data.decode())
                text = " ".join(page.parts)
            originals[key] = " ".join(text.split())
        if quote not in originals[key]:
            raise ValueError(f"Unmatched source span in {key}: {quote}")


def embeddings(directory, passages):
    model = ort.SessionOptions()
    model.intra_op_num_threads = model.inter_op_num_threads = 1
    runner = ort.InferenceSession(verified(directory, "minilm.onnx"), model,
                                  providers=["CPUExecutionProvider"])
    tokenizer = Tokenizer.from_str(verified(directory, "tokenizer.json").decode())
    tokenizer.enable_truncation(max_length=256)
    tokenizer.enable_padding()
    blocks = []
    for pos in range(0, len(passages), 8):
        encoded = tokenizer.encode_batch(passages[pos:pos + 8])
        ids = np.array([e.ids for e in encoded], dtype=np.int64)
        mask = np.array([e.attention_mask for e in encoded], dtype=np.int64)
        hidden = runner.run(None, {"input_ids": ids, "attention_mask": mask,
                                   "token_type_ids": np.zeros_like(ids)})[0]
        pooled = (hidden * mask[:, :, None]).sum(axis=1) / mask.sum(axis=1)[:, None]
        blocks.append(pooled / np.linalg.norm(pooled, axis=1, keepdims=True))
    return np.vstack(blocks)


def lexical(texts, query):
    db = sqlite3.connect(":memory:")
    try:
        db.execute("CREATE VIRTUAL TABLE p USING fts5(id UNINDEXED, body, tokenize='porter')")
        db.executemany("INSERT INTO p VALUES (?,?)", texts)
        words = re.findall(r"[a-z0-9]+", query.lower())
        match = " OR ".join(f'"{w}"' for w in words)
        rows = db.execute("SELECT id FROM p WHERE p MATCH ? ORDER BY bm25(p), id, rowid", (match,))
        return list(dict.fromkeys(r[0] for r in rows))
    finally:
        db.close()


def retrieval(directory, compare_agreement=False):
    check_quotes(directory)
    dates, bills, texts = {}, {}, []
    for edition, date in CASES["editions"].items():
        for number, body in provision_texts(directory, edition).items():
            key = f"{edition}/{number}"
            bills[key] = re.fullmatch(r"(\d+(?:hr|s)\d+)[a-z]+", edition)[1]
            dates[key] = date
            words = body.split()
            texts.extend((key, " ".join(words[i:i + 120])) for i in range(0, len(words), 100))
    for vote, date in CASES["votes"].items():
        bills[vote], body = vote_document(directory, vote)
        dates[vote] = date
        texts.append((vote, body))

    cases = CASES["retrieval"]
    vectors = embeddings(directory, [t for _, t in texts] + [q[4] for q in cases])
    for index, (name, cutoff, _, _, query, gold) in enumerate(cases):
        eligible = [i for i, (key, _) in enumerate(texts) if dates[key] <= cutoff]
        if any(key not in dates or dates[key] > cutoff for key in gold):
            raise ValueError(f"Ineligible gold reference in {name}")
        ranked = lexical([texts[i] for i in eligible], query)
        scores = vectors[eligible] @ vectors[len(texts) + index]
        dense = list(dict.fromkeys(texts[eligible[i]][0] for i in np.argsort(-scores, kind="stable")))
        sections_first = [key for key in ranked if "/" in key][:2]
        linked = []
        if sections_first:
            anchor = sections_first[0]
            votes = [v for v in CASES["votes"] if dates[v] <= cutoff and bills[v] == bills[anchor]]
            linked = sorted(votes, key=lambda v: (ranked.index(v) if v in ranked else len(ranked), v))
        paths = list(dict.fromkeys(sections_first + linked + ranked))
        if compare_agreement:
            lexical_first = next((key for key in ranked if "/" in key), None)
            dense_first = next((key for key in dense if "/" in key), None)
            same_section = lexical_first is not None and lexical_first == dense_first
            same_bill = lexical_first is not None and dense_first is not None and bills[lexical_first] == bills[dense_first]
            print(name, "answerable" if gold else "insufficient", lexical_first, dense_first,
                  f"same bill={same_bill}", f"same edition and section={same_section}",
                  f"complete set in five={bool(gold) and set(gold) <= set(paths[:5])}", sep=" | ")
            continue
        for method, hits in [("BM25", ranked), ("MiniLM", dense), ("BM25+vote", paths)]:
            print(name, method, "complete" if gold and set(gold) <= set(hits[:5]) else
                  ("incomplete" if gold else "no sufficient set"), hits[:5], sep=" | ")


def lineage(directory):
    cache = {}
    for before, num, after, expected, label in CASES["lineage"]:
        for edition in [before, after]:
            if edition not in cache:
                cache[edition] = provision_texts(directory, edition)
        target = cache[after]
        if expected is not None and expected not in target:
            raise ValueError(expected)
        keys = sorted(target)
        matrix = TfidfVectorizer(stop_words="english", sublinear_tf=True).fit_transform(
            [target[k] for k in keys] + [cache[before][num]])
        scores = (matrix[:-1] @ matrix[-1].T).toarray().ravel()
        idx = int(np.argmax(scores))
        guess = keys[idx] if scores[idx] >= 0.30 else None
        print(label, f"reference={expected}", f"same number={num if num in target else None}",
              f"text={guess}", f"cosine={scores[idx]:.3f}", sep=" | ")


if __name__ == "__main__":
    folder = Path(sys.argv[1])
    if sys.argv[2] in ("retrieval", "agreement"):
        retrieval(folder, compare_agreement=sys.argv[2] == "agreement")
    elif sys.argv[2] == "lineage":
        lineage(folder)
    else:
        raise SystemExit("Choose retrieval, agreement or lineage.")
