// Shared dedupe helpers for aggregation surfaces.
//
// Aggregation tables (recently flagged posts, supporting-docs, narrative
// example posts) consume rows joined across multiple analysis runs per
// doc — when a doc has more than one run for the same task, the joined
// query returns the doc once per run. Dedupe on the doc-level identifier
// so readers never see the same tweet ten times in a row.

export function dedupeById<T>(items: readonly T[], getId: (item: T) => unknown): T[] {
    const seen = new Set<unknown>();
    const out: T[] = [];
    for (const item of items) {
        const id = getId(item);
        if (id === undefined || id === null) {
            out.push(item);
            continue;
        }
        if (seen.has(id)) continue;
        seen.add(id);
        out.push(item);
    }
    return out;
}
