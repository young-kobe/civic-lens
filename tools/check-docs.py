#!/usr/bin/env python3
"""Check repository paths, local links, and sentence length in the core prose docs."""
import argparse
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MAX_WORDS = 25
GENERATED_SCHEMA = 'docs/architecture/schema/'
PATH_PREFIXES = ('src/', 'tests/', 'tools/', 'config/', 'docs/')
CODE_SPAN = re.compile(r'`[^`]*`')
LINK = re.compile(r'(?<!!)\[([^\]]*)\]\(([^)\s]+)\)')
HEADING = re.compile(r'^(#{1,6})\s+(.*?)\s*#*\s*$')
SENTENCE_END = re.compile(r'(?<=[.!?])\s+(?=[A-Z0-9`"(\[*])')


def prose_files():
    files = [ROOT / 'README.md', ROOT / 'AGENTS.md', *sorted((ROOT / 'docs').rglob('*.md'))]
    return [path for path in files if path.exists()]


def sentence_checked(path):
    relative = path.relative_to(ROOT).as_posix()
    if relative.startswith(GENERATED_SCHEMA):
        return False
    return relative == 'docs/architecture.md' or relative.startswith('docs/architecture/')


def slug(heading):
    text = LINK.sub(r'\1', heading).lower()
    text = re.sub(r'[^\w\- ]', '', text)
    return text.replace(' ', '-')


ANCHORS = {}


def anchors(path):
    if path not in ANCHORS:
        found, counts = set(), {}
        for _, line, _ in content_lines(path):
            match = HEADING.match(line)
            if match:
                base = slug(match.group(2))
                count = counts.get(base, 0)
                counts[base] = count + 1
                found.add(f'{base}-{count}' if count else base)
        ANCHORS[path] = found
    return ANCHORS[path]


def content_lines(path):
    """Yield (line number, text, region) outside fenced code; region marks generated diagrams."""
    fenced = False
    generated = False
    for number, line in enumerate(path.read_text().splitlines(), 1):
        if line.startswith('```'):
            fenced = not fenced
            continue
        if '<!-- diagrams:start -->' in line:
            generated = True
        if '<!-- diagrams:end -->' in line:
            generated = False
        if not fenced:
            yield number, line, generated


def check_paths(path, number, line):
    for span in CODE_SPAN.findall(line):
        candidate = span.strip('`').rstrip('.,;:')
        if not candidate.startswith(PATH_PREFIXES) or re.search(r'[<>{}*\s|]', candidate):
            continue
        if not (ROOT / candidate).exists():
            yield f'{path.relative_to(ROOT)}:{number}: missing path {candidate}'


def check_links(path, number, line):
    for _, target in LINK.findall(CODE_SPAN.sub('', line)):
        if re.match(r'[a-z]+:', target):
            continue
        file_part, _, anchor = target.partition('#')
        destination = (path.parent / file_part).resolve() if file_part else path
        if not destination.exists():
            yield f'{path.relative_to(ROOT)}:{number}: broken link {target}'
        elif anchor and destination.suffix == '.md' and anchor not in anchors(destination):
            yield f'{path.relative_to(ROOT)}:{number}: missing anchor {target}'


def check_sentences(path, number, line):
    text = line.strip()
    if not text or text.startswith(('#', '|', '<!--')):
        return
    text = re.sub(r'^([-*]|\d+\.)\s+', '', text)
    text = CODE_SPAN.sub('CODE', LINK.sub(r'\1', text))
    for sentence in SENTENCE_END.split(text):
        words = len(sentence.split())
        if words > MAX_WORDS:
            yield f'{path.relative_to(ROOT)}:{number}: sentence has {words} words (limit {MAX_WORDS})'


def failures():
    for path in prose_files():
        for number, line, generated in content_lines(path):
            yield from check_paths(path, number, line)
            yield from check_links(path, number, line)
            if sentence_checked(path) and not generated:
                yield from check_sentences(path, number, line)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='exit with status 1 when any check fails')
    args = parser.parse_args()
    found = list(failures())
    for failure in found:
        print(failure)
    if found:
        message = f'{len(found)} documentation check failures.'
        if args.check:
            raise SystemExit(message)
        print(message)
        return
    print('Documentation paths, links, and sentence lengths pass.')


if __name__ == '__main__':
    main()
