#!/usr/bin/env python3
"""Assign test classes to CI shards and write the runsettings filter for one shard."""
import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TEST_PROJECT = 'tests/CivicLens.Tests'
TIMINGS = ROOT / 'tools' / 'test-shard-timings.txt'
UNKNOWN_SECONDS = 1


def list_classes():
    result = subprocess.run(
        ['dotnet', 'test', TEST_PROJECT, '--configuration', 'Release', '--no-build', '--list-tests'],
        cwd=ROOT, capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(result.stdout + result.stderr)
    classes = set()
    for line in result.stdout.splitlines():
        if line.startswith('    '):
            name = line.strip().split('(')[0]
            classes.add(name.rsplit('.', 1)[0])
    if not classes:
        sys.exit('No tests found. Build the test project first.')
    return sorted(classes)


def read_timings(classes):
    timings = {}
    for line in TIMINGS.read_text().splitlines():
        name, seconds = line.rsplit(' ', 1)
        timings[name] = int(seconds)
    stale = sorted(set(timings) - set(classes))
    if stale:
        sys.exit(f'{TIMINGS.name} lists classes that no longer exist: ' + ', '.join(stale))
    return timings


def assign(classes, count):
    timings = read_timings(classes)
    totals = [0] * count
    shards = [[] for _ in range(count)]
    known = sorted(timings, key=lambda name: (-timings[name], name))
    unknown = [name for name in classes if name not in timings]
    for name in known + unknown:
        index = totals.index(min(totals))
        shards[index].append(name)
        totals[index] += timings.get(name, UNKNOWN_SECONDS)
    return shards, totals


def shard_classes(classes, index, count):
    return sorted(assign(classes, count)[0][index])


def filter_for(classes):
    return '|'.join(f'FullyQualifiedName~{name}.' for name in classes)


def write_runsettings(path, classes):
    expression = filter_for(classes).replace('&', '&amp;').replace('<', '&lt;')
    path.write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        f'<RunSettings><RunConfiguration><TestCaseFilter>{expression}</TestCaseFilter>'
        '</RunConfiguration></RunSettings>\n')


def check(classes, count):
    seen = {}
    for index in range(count):
        for name in shard_classes(classes, index, count):
            if name in seen:
                sys.exit(f'{name} is in shard {seen[name]} and shard {index}.')
            seen[name] = index
    missing = sorted(set(classes) - set(seen))
    if missing:
        sys.exit('Classes in no shard: ' + ', '.join(missing))
    totals = assign(classes, count)[1]
    print(f'{len(classes)} test classes in {count} shards. Weighted seconds per shard: {totals}.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--shards', type=int, required=True)
    parser.add_argument('--index', type=int, help='Shard number, from 0.')
    parser.add_argument('--runsettings', type=Path, help='Write the filter for --index to this file.')
    parser.add_argument('--check', action='store_true', help='Check that each class is in exactly one shard.')
    args = parser.parse_args()
    classes = list_classes()
    if args.check:
        check(classes, args.shards)
        return
    if args.index is None or not 0 <= args.index < args.shards or args.runsettings is None:
        parser.error('Give --index from 0 to shards minus 1, and --runsettings.')
    chosen = shard_classes(classes, args.index, args.shards)
    write_runsettings(args.runsettings, chosen)
    print(f'Shard {args.index} of {args.shards}: {len(chosen)} test classes.')


if __name__ == '__main__':
    main()
