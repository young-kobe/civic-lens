#!/usr/bin/env python3
"""Assign test classes to CI shards and write the runsettings filter for one shard."""
import argparse
import subprocess
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TEST_PROJECT = 'tests/CivicLens.Tests'


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


def shard_of(class_name, count):
    return zlib.crc32(class_name.encode()) % count


def shard_classes(classes, index, count):
    return [name for name in classes if shard_of(name, count) == index]


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
    parser.add_argument('--shards', type=int, required=True)
    parser.add_argument('--index', type=int, required=True, help='Shard number, from 0.')
    parser.add_argument('--runsettings', type=Path, required=True, help='Write the filter for --index to this file.')
    args = parser.parse_args()
    if not 0 <= args.index < args.shards:
        parser.error('Give --index from 0 to shards minus 1.')
    chosen = shard_classes(list_classes(), args.index, args.shards)
    if not chosen:
        sys.exit(f'Shard {args.index} of {args.shards} has no test classes. Use fewer shards.')
    write_runsettings(args.runsettings, chosen)
    print(f'Shard {args.index} of {args.shards}: {len(chosen)} test classes.')


if __name__ == '__main__':
    main()
