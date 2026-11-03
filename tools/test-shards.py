#!/usr/bin/env python3
"""Assign test classes to CI shards and write the runsettings filter for one shard."""
import argparse
import json
import statistics
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TEST_PROJECT = 'tests/CivicLens.Tests'
WEIGHTS_FILE = ROOT / 'tools' / 'test-shard-weights.json'
TRX_NAMESPACE = '{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'


def class_of(test_name):
    return test_name.split('(')[0].rsplit('.', 1)[0]


def list_classes():
    result = subprocess.run(
        ['dotnet', 'test', TEST_PROJECT, '--configuration', 'Release', '--no-build', '--list-tests'],
        cwd=ROOT, capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(result.stdout + result.stderr)
    classes = set()
    for line in result.stdout.splitlines():
        if line.startswith('    '):
            classes.add(class_of(line.strip()))
    if not classes:
        sys.exit('No tests found. Build the test project first.')
    return sorted(classes)


def load_weights():
    return json.loads(WEIGHTS_FILE.read_text())


def shard_classes(classes, index, count):
    weights = load_weights()
    default = statistics.median(weights.values()) if weights else 1.0
    ordered = sorted(classes, key=lambda name: (-weights.get(name, default), name))
    totals = [0.0] * count
    members = [[] for _ in range(count)]
    for name in ordered:
        lightest = totals.index(min(totals))
        totals[lightest] += weights.get(name, default)
        members[lightest].append(name)
    return sorted(members[index])


def read_trx_seconds(paths):
    seconds = defaultdict(float)
    for path in paths:
        for result in ET.parse(path).iter(f'{TRX_NAMESPACE}UnitTestResult'):
            hours, minutes, secs = result.get('duration', '0:0:0').split(':')
            duration = int(hours) * 3600 + int(minutes) * 60 + float(secs)
            seconds[class_of(result.get('testName'))] += duration
    return seconds


def write_weights(paths):
    seconds = read_trx_seconds(paths)
    if not seconds:
        sys.exit('No test results found in the TRX files.')
    table = {name: round(seconds[name], 1) for name in sorted(seconds)}
    WEIGHTS_FILE.write_text(json.dumps(table, indent=2) + '\n')
    print(f'Wrote {len(table)} class weights to {WEIGHTS_FILE.relative_to(ROOT)}.')


def filter_for(classes):
    return '|'.join(f'FullyQualifiedName~{name}.' for name in classes)


def write_runsettings(path, classes):
    expression = filter_for(classes).replace('&', '&amp;').replace('<', '&lt;')
    path.write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        f'<RunSettings><RunConfiguration><TreatNoTestsAsError>true</TreatNoTestsAsError><TestCaseFilter>{expression}</TestCaseFilter>'
        '</RunConfiguration></RunSettings>\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--shards', type=int)
    parser.add_argument('--index', type=int, help='Shard number, from 0.')
    parser.add_argument('--runsettings', type=Path, help='Write the filter for --index to this file.')
    parser.add_argument('--write-weights', nargs='+', type=Path, metavar='TRX',
                        help='Rewrite the weight table from the TRX files of one full run.')
    args = parser.parse_args()
    if args.write_weights:
        write_weights(args.write_weights)
        return
    if args.shards is None or args.index is None or args.runsettings is None:
        parser.error('Give --shards, --index, and --runsettings, or give --write-weights.')
    if not 0 <= args.index < args.shards:
        parser.error('Give --index from 0 to shards minus 1.')
    chosen = shard_classes(list_classes(), args.index, args.shards)
    if not chosen:
        sys.exit(f'Shard {args.index} of {args.shards} has no test classes. Use fewer shards.')
    write_runsettings(args.runsettings, chosen)
    print(f'Shard {args.index} of {args.shards}: {len(chosen)} test classes.')


if __name__ == '__main__':
    main()
