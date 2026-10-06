#!/usr/bin/env python3
"""Generate Mermaid references and an offline HTML explorer from one graph source."""
import argparse
import html
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VIEWS = [
    {
        "title": "Collection to publication (target)",
        "description": "Target workflow. Configuration, bounded watched-page collection, verified captures, and the Postgres import adapter are implemented; CLI migration, collection/import, and saved receipt recovery are implemented; managed durable jobs are implemented; downstream stages remain planned.",
        "nodes": [
            ("config", "Coverage configuration", "People, dated roles, sources, issues and policies; one authoritative configuration path."),
            ("app", "C# application", "Owns durable jobs, aggregate budgets, retries and orchestration."),
            ("collector", "C# collector", "Separate executable. Bounded HTTP/feed discovery and immutable captures; no database or AI."),
            ("evidence", "Evidence store", "Local Postgres import adapter and content-addressed files are implemented. CLI migration, collection/import, and filesystem receipt handoff/replay are implemented. Document versions remain planned."),
            ("analysis", "Selective analysis", "Deterministic records first; evaluated classification and evidence extraction only where useful."),
            ("review", "Local review and MCP", "Inspect evidence and proposed changes. Human decisions bind to exact versions."),
            ("release", "Versioned release", "Approved records, static pages and search index. Validate citations before switching the active release."),
            ("public", "Website and public MCP", "Read published releases only. Never crawl, call models or access local operational data."),
        ],
        "edges": [("config", "app"), ("app", "collector"), ("collector", "evidence"), ("evidence", "analysis"), ("analysis", "review"), ("review", "release"), ("release", "public")],
    },
    {
        "title": "Project dependencies (implemented)",
        "description": "Arrows mean references. These project boundaries are checked by xUnit tests.",
        "nodes": [
            ("host", "Host", "CLI entry point and future local review/MCP composition root."),
            ("infra", "Infrastructure", "Collector process execution, capture verification, and EF Core/Npgsql persistence. References Application."),
            ("app", "Application", "Use cases and external interfaces; no infrastructure reference."),
            ("core", "Core", "Domain invariants. No project or third-party dependencies."),
            ("cc", "Collection.Contracts", "Versioned watched-page requests, capture/response metadata and receipt validation. No dependencies."),
            ("pc", "Publication.Contracts", "Future public release contract. No dependencies."),
            ("collector", "Collector", "Independent executable referencing Collection.Contracts only."),
        ],
        "edges": [("host", "app"), ("host", "infra"), ("infra", "app"), ("app", "core"), ("app", "cc"), ("app", "pc"), ("collector", "cc")],
    },
]


def markdown():
    blocks = []
    for view in VIEWS:
        lines = [f'### {view["title"]}', "", view["description"], "", "```mermaid", "flowchart LR"]
        lines += [f'    {key}["{label}"]' for key, label, _ in view["nodes"]]
        lines += [f"    {a} --> {b}" for a, b in view["edges"]]
        lines += ["```", ""]
        blocks.append("\n".join(lines))
    return "\n".join(blocks)


def svg(view):
    positions = {node[0]: (35 + (i % 2) * 410, 35 + (i // 2) * 135) for i, node in enumerate(view["nodes"])}
    height = ((len(view["nodes"]) + 1) // 2) * 135 + 30
    parts = [f'<svg viewBox="0 0 820 {height}" role="img" aria-label="{html.escape(view["title"])}">', '<defs><marker id="arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8" fill="#7d9cbb"/></marker></defs>']
    for a, b in view["edges"]:
        x1, y1 = positions[a]
        x2, y2 = positions[b]
        parts.append(f'<path d="M{x1+155},{y1+64} C{x1+155},{y1+95} {x2+155},{y2-30} {x2+155},{y2}" fill="none" stroke="#7d9cbb" stroke-width="2" marker-end="url(#arrow)"/>')
    for key, label, description in view["nodes"]:
        x, y = positions[key]
        parts.append(f'<g tabindex="0" role="button" aria-label="{html.escape(label)}" data-detail="{html.escape(description, quote=True)}"><rect x="{x}" y="{y}" width="310" height="64" rx="10"/><text x="{x+155}" y="{y+38}" text-anchor="middle">{html.escape(label)}</text></g>')
    return "".join(parts) + "</svg>"


def render_html():
    panels = []
    for i, view in enumerate(VIEWS):
        panels.append(f'<section id="view-{i}" {"hidden" if i else ""}><h2>{html.escape(view["title"])}</h2><p>{html.escape(view["description"])}</p>{svg(view)}<ul>' + ''.join(f'<li><strong>{html.escape(label)}:</strong> {html.escape(desc)}</li>' for _, label, desc in view['nodes']) + '</ul></section>')
    options = ''.join(f'<option value="view-{i}">{html.escape(v["title"])}</option>' for i, v in enumerate(VIEWS))
    return '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Civic Lens architecture</title>
<style>body{margin:0;background:#101b2b;color:#e3ecf6;font:16px/1.6 system-ui,sans-serif}main{max-width:1000px;margin:auto;padding:32px}h1{font-size:38px;margin-bottom:8px}h2{font-size:24px}p,li{color:#bacee1}select{padding:10px;max-width:100%;background:#20334b;color:white;border:1px solid #7d9cbb;border-radius:6px}svg{width:100%;height:auto}rect{fill:#20334b;stroke:#71c6bc;stroke-width:2}text{fill:#e3ecf6;font-size:17px}g{cursor:pointer}g:focus rect,g:hover rect{fill:#30536b;stroke:#fff}aside{padding:18px;border-left:3px solid #71c6bc;background:#17283e}li{margin:8px 0}[hidden]{display:none}</style>
<main><p>CIVIC LENS / ENGINEERING REFERENCE</p><h1>Evidence, with clear boundaries.</h1><p>Generated from the same definitions as docs/architecture.md. This explorer separates the implemented tracer from the planned system.</p><label for="view">Diagram </label><select id="view">''' + options + '</select>' + ''.join(panels) + '''<aside id="detail" aria-live="polite">Select a component to inspect its responsibility.</aside><p>Regenerate: <code>python3 tools/render-architecture.py</code></p></main>
<script>document.querySelector('#view').addEventListener('change',e=>{document.querySelectorAll('section').forEach(s=>s.hidden=s.id!==e.target.value);document.querySelector('#detail').textContent='Select a component to inspect its responsibility.'});document.querySelectorAll('[data-detail]').forEach(n=>{const show=()=>document.querySelector('#detail').textContent=n.dataset.detail;n.addEventListener('click',show);n.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();show()}})});</script></html>'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    path = ROOT / 'docs/architecture.md'
    original = path.read_text()
    start, tail = original.split('<!-- diagrams:start -->', 1)
    _, end = tail.split('<!-- diagrams:end -->', 1)
    expected = start + '<!-- diagrams:start -->\n' + markdown() + '\n<!-- diagrams:end -->' + end
    if args.check:
        if original != expected:
            raise SystemExit('Architecture diagrams are stale; run tools/render-architecture.py.')
        print('Architecture diagrams match their source definitions.')
        return
    path.write_text(expected)
    output = ROOT / 'artifacts/architecture.html'
    output.parent.mkdir(exist_ok=True)
    output.write_text(render_html())
    print(output)


if __name__ == '__main__':
    main()
