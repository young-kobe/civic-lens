"""Read only the public inputs named in the study manifest."""
import hashlib
import json
from html.parser import HTMLParser
import sys
from pathlib import Path
from urllib.request import Request, urlopen

FOLDER = Path(__file__).resolve().parent
SOURCES = json.loads((FOLDER / "sources.json").read_text())
CASES = json.loads((FOLDER / "cases.json").read_text())


class Paragraphs(HTMLParser):
    def __init__(self):
        super().__init__()
        self.paragraphs = []
        self.parts = None

    def handle_starttag(self, tag, attrs):
        if tag == "p":
            self.parts = []

    def handle_data(self, data):
        if self.parts is not None:
            self.parts.append(data)

    def handle_endtag(self, tag):
        if tag == "p" and self.parts is not None:
            self.paragraphs.append(" ".join("".join(self.parts).split()))
            self.parts = None


def quote_digest(key, data):
    page = Paragraphs()
    page.feed(data.decode())
    selected = []
    for case in CASES["retrieval"]:
        if case[2] != key:
            continue
        matches = [p for p in page.paragraphs if case[4] in p]
        if len(matches) != 1:
            raise ValueError(f"Ambiguous or missing quote in {key}")
        selected.extend(matches)
    return hashlib.sha256("\n".join(selected).encode()).hexdigest()


def check(key, data):
    source = SOURCES[key]
    if "paragraph_sha256" in source:
        if quote_digest(key, data) != source["paragraph_sha256"]:
            raise ValueError(f"Cited paragraphs changed for {key}")
    elif hashlib.sha256(data).hexdigest() != source["sha256"]:
        raise ValueError(f"Source bytes changed for {key}")


def verified(directory, key):
    data = (directory / key).read_bytes()
    check(key, data)
    return data


if __name__ == "__main__":
    directory = Path(sys.argv[1])
    directory.mkdir(exist_ok=True, parents=True)
    for key, source in SOURCES.items():
        path = directory / key
        if not path.exists():
            request = Request(source["url"], headers={"User-Agent": "CivicLens research replay"})
            with urlopen(request, timeout=60) as response:
                body = response.read()
            check(key, body)
            path.write_bytes(body)
        verified(directory, key)
