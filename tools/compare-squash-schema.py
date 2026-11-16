#!/usr/bin/env python3
"""Build the operational schema from the pre-squash migration chain and from the baseline, then compare them.

Database A comes from `db migrate` at the pre-squash commit. Database B comes from `db migrate` on the working tree. Each database
lives in its own Postgres container, so the roles of B come from the baseline and never from A. The compare covers the
`pg_dump --schema-only` text with the columns of each table sorted, the view definitions, the relation options, the function
bodies, the triggers, the indexes, the roles, the table, column, schema, and function grants, and the default privileges.
Column order is reported but is not a failure. The script exits with 1 when any other part differs.

It needs Docker, git, and the .NET SDK of global.json. It never reads production.
"""
import argparse
import difflib
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PRE_SQUASH_COMMIT = '4969ed76'
IMAGE = 'postgres:18.3-alpine@sha256:54451ecb8ab38c24c3ec123f2fd501303a3a1856a5c66e98cecf2460d5e1e9d7'
DATABASE = 'squash_compare'
USER = 'compare'

# Each check is one catalog query. The rows are sorted text, so the order of the catalog never matters.
CHECKS = {
    'view definitions': """
        SELECT schemaname || '.' || viewname || E'\\n' || definition FROM pg_views
         WHERE schemaname NOT IN ('pg_catalog', 'information_schema')""",
    'relation options': """
        SELECT n.nspname || '.' || c.relname || ' ' || c.relkind::text || ' ' || coalesce(array_to_string(c.reloptions, ','), '')
               || ' owner=' || pg_get_userbyid(c.relowner)
          FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')""",
    'function bodies': """
        SELECT n.nspname || '.' || p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')' || E'\\n' || pg_get_functiondef(p.oid)
          FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
         WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')""",
    'triggers': """
        SELECT pg_get_triggerdef(t.oid) FROM pg_trigger t WHERE NOT t.tgisinternal""",
    'indexes': """
        SELECT pg_get_indexdef(i.indexrelid) FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
          JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')""",
    'constraints': """
        SELECT c.conrelid::regclass || ' ' || c.conname || ' ' || pg_get_constraintdef(c.oid) FROM pg_constraint c
          JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')""",
    'column definitions': """
        SELECT table_schema || '.' || table_name || '.' || column_name || ' ' || data_type || ' ' || coalesce(character_maximum_length::text, '')
               || ' null=' || is_nullable || ' default=' || coalesce(column_default, '') || ' identity=' || is_identity
          FROM information_schema.columns WHERE table_schema NOT IN ('pg_catalog', 'information_schema')""",
    'roles': """
        SELECT rolname || ' super=' || rolsuper || ' inherit=' || rolinherit || ' createrole=' || rolcreaterole || ' createdb=' || rolcreatedb
               || ' login=' || rolcanlogin || ' limit=' || rolconnlimit || ' config=' || coalesce(array_to_string(rolconfig, ','), '')
          FROM pg_roles WHERE rolname LIKE 'civic_lens%'""",
    'role memberships': """
        SELECT r.rolname || ' in ' || g.rolname FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.member JOIN pg_roles g ON g.oid = m.roleid
         WHERE r.rolname LIKE 'civic_lens%' OR g.rolname LIKE 'civic_lens%'""",
    'table grants': """
        SELECT grantee || ' ' || privilege_type || ' ' || table_schema || '.' || table_name FROM information_schema.role_table_grants
         WHERE grantee LIKE 'civic_lens%' OR grantee = 'PUBLIC'""",
    'column grants': """
        SELECT grantee || ' ' || privilege_type || ' ' || table_schema || '.' || table_name || '.' || column_name
          FROM information_schema.column_privileges WHERE grantee LIKE 'civic_lens%' OR grantee = 'PUBLIC'""",
    'schema grants': """
        SELECT nspname || ' ' || coalesce(array_to_string(nspacl, ','), '') || ' owner=' || pg_get_userbyid(nspowner) FROM pg_namespace
         WHERE nspname NOT LIKE 'pg\\_%' AND nspname <> 'information_schema'""",
    'function grants': """
        SELECT p.oid::regprocedure || ' ' || coalesce(array_to_string(p.proacl, ','), '') FROM pg_proc p WHERE p.proacl IS NOT NULL""",
    'database grants': """
        SELECT coalesce(array_to_string(datacl, ','), '') FROM pg_database WHERE datname = current_database()""",
    'default privileges': """
        SELECT pg_get_userbyid(defaclrole) || ' ' || defaclobjtype::text || ' ' || coalesce(defaclnamespace::regnamespace::text, '') || ' '
               || array_to_string(defaclacl, ',') FROM pg_default_acl""",
}

COLUMN_ORDER = """
    SELECT table_schema || '.' || table_name, string_agg(column_name, ',' ORDER BY ordinal_position)
      FROM information_schema.columns WHERE table_schema = 'public' AND table_name IN
           (SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE')
     GROUP BY 1"""


def run(command, **options):
    return subprocess.run(command, check=True, text=True, capture_output=True, **options).stdout


def start_postgres(name):
    subprocess.run(['docker', 'rm', '-f', name], capture_output=True)
    port = run(['docker', 'run', '-d', '--name', name, '-e', f'POSTGRES_USER={USER}', '-e', f'POSTGRES_PASSWORD={USER}',
                '-e', f'POSTGRES_DB={DATABASE}', '-p', '127.0.0.1::5432', IMAGE])
    for _ in range(60):
        if subprocess.run(['docker', 'exec', name, 'pg_isready', '-U', USER, '-d', DATABASE], capture_output=True).returncode == 0:
            break
        time.sleep(1)
    time.sleep(2)
    port = run(['docker', 'port', name, '5432/tcp']).strip().split(':')[-1]
    return f'Host=127.0.0.1;Port={port};Database={DATABASE};Username={USER};Password={USER}'


def migrate(tree, connection):
    environment = dict(os.environ, CIVIC_LENS_DATABASE=connection)
    run(['dotnet', 'build', 'src/CivicLens.Host', '-c', 'Release'], cwd=tree)
    run(['dotnet', 'run', '--no-build', '-c', 'Release', '--project', 'src/CivicLens.Host', '--', 'db', 'migrate'], cwd=tree, env=environment)


def query(container, sql):
    rows = run(['docker', 'exec', '-i', container, 'psql', '-U', USER, '-d', DATABASE, '-AtX', '-v', 'ON_ERROR_STOP=1',
                '-c', f'SELECT x FROM ({sql}) AS q(x) ORDER BY 1'])
    return rows.splitlines()


def dump(container):
    text = run(['docker', 'exec', container, 'pg_dump', '-U', USER, '-d', DATABASE, '--schema-only'])
    kept = [line for line in text.split('\n') if line.strip() and not line.startswith(('--', '\\restrict', '\\unrestrict'))]
    # pg_dump writes the columns of a table and the column grants in column order. Sort both, so only content differs.
    statements = []
    for statement in '\n'.join(kept).split(';\n'):
        lines = statement.split('\n')
        if lines[0].startswith('CREATE TABLE '):
            lines = [lines[0], *sorted(line.rstrip(',') for line in lines[1:-1]), lines[-1]]
        statements.append('\n'.join(lines) + ';')
    return [line for statement in sorted(statements) for line in statement.split('\n')]


def column_orders(container):
    rows = run(['docker', 'exec', '-i', container, 'psql', '-U', USER, '-d', DATABASE, '-AtX', '-F', '|', '-c', COLUMN_ORDER])
    return dict(line.split('|', 1) for line in rows.split('\n') if line)


def compare(container_a, container_b):
    differences = 0
    for name, lines in [('pg_dump --schema-only', None), *CHECKS.items()]:
        a = dump(container_a) if lines is None else query(container_a, lines)
        b = dump(container_b) if lines is None else query(container_b, lines)
        diff = list(difflib.unified_diff(a, b, 'A (chain)', 'B (baseline)', lineterm='', n=1))
        print(f'{name}: {len(a)} rows in A, {len(b)} rows in B, ' + ('same' if not diff else 'DIFFERENT'))
        if diff:
            differences += 1
            print('\n'.join(diff))
    order_a, order_b = column_orders(container_a), column_orders(container_b)
    moved = sorted(table for table in order_a if order_b.get(table) != order_a[table]
                   and sorted(order_b.get(table, '').split(',')) == sorted(order_a[table].split(',')))
    print(f'column order: {len(order_a)} tables in A, {len(order_b)} tables in B, {len(moved)} with another column order')
    for table in moved:
        print(f'  {table}')
    return differences


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--old-ref', default=PRE_SQUASH_COMMIT, help='the commit that holds the pre-squash migration chain')
    parser.add_argument('--keep', action='store_true', help='keep both containers after the compare')
    arguments = parser.parse_args()
    container_a, container_b = 'civic-lens-squash-a', 'civic-lens-squash-b'
    with tempfile.TemporaryDirectory() as directory:
        old_tree = Path(directory) / 'chain'
        run(['git', 'worktree', 'add', '--detach', str(old_tree), arguments.old_ref], cwd=ROOT)
        try:
            print(f'Building database A from the chain at {arguments.old_ref} and database B from the working tree.')
            migrate(old_tree, start_postgres(container_a))
            migrate(ROOT, start_postgres(container_b))
            differences = compare(container_a, container_b)
        finally:
            run(['git', 'worktree', 'remove', '--force', str(old_tree)], cwd=ROOT)
            if not arguments.keep:
                subprocess.run(['docker', 'rm', '-f', container_a, container_b], capture_output=True)
    print(f'{differences} parts differ apart from column order.')
    return 1 if differences else 0


if __name__ == '__main__':
    sys.exit(main())
