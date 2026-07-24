import { useMemo, useState } from 'react';
import { Card, EmptyState, ErrorState, LoadingCard, MethodPopover, MoversTicker, RangeCaption } from '../components/common';
import { Sparkline } from '../components/charts';
import {
    fetchBotActivity, fetchEvalAccuracy, fetchMovers, fetchNarratives,
    fetchPropaganda, fetchSentiment, fetchSnapshotStatus, type MoversWindow,
} from '../services/api';
import { formatRefreshedAgo } from '../services/freshness';
import { formatCount, formatPct, formatPts } from '../services/format';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import type {
    BotActivityResponse, EvalAccuracy, Filters, MoversResponse, NarrativesResponse,
    PropagandaOverview, SentimentPanelResponse, SnapshotStatusResponse,
} from '../types';

// --------------------------------------------------------------------------- //
//  Data Desk — the numbers-forward page.                                     //
//                                                                             //
//  Phase 10 adaptation note: the pre-redesign cross-signal matrix joined     //
//  sentiment/propaganda/bots/narratives on a shared per-entity `kind:key`.    //
//  The strictly-live contract no longer gives every panel the same entity    //
//  identifier (sentiment keys entities by numeric entity_id, bots by the     //
//  entity_key string, and propaganda/narratives carry no entity breakdown    //
//  at all) -- this rebuild joins sentiment x bots on (kind, displayName) as  //
//  the closest available proxy, and drops the propaganda/narratives columns  //
//  the join can no longer support.                                          //
// --------------------------------------------------------------------------- //

interface MatrixRow {
    id: string;
    name: string;
    kind: string;
    netTone: number | null;
    posts: number | null;
    botRate: number | null;
}

type MatrixSortKey = 'name' | 'netTone' | 'posts' | 'botRate';

function buildMatrix(
    sentiment: SentimentPanelResponse | null,
    bots: BotActivityResponse | null,
): MatrixRow[] {
    const rows = new Map<string, MatrixRow>();
    const ensure = (kind: string | null, name: string): MatrixRow => {
        const id = `${kind ?? 'unresolved'}:${name}`;
        let row = rows.get(id);
        if (!row) {
            row = { id, name, kind: kind ?? 'unresolved', netTone: null, posts: null, botRate: null };
            rows.set(id, row);
        }
        return row;
    };

    for (const e of sentiment?.entityStances ?? []) {
        if (e.entityId == null) continue;
        const row = ensure(e.kind, e.displayName);
        row.netTone = e.favorability.netScore;
        row.posts = e.favorability.volume;
    }
    for (const e of bots?.byEntity ?? []) {
        const row = ensure(e.kind, e.displayName);
        row.botRate = e.botRatePct;
    }
    return Array.from(rows.values());
}

function compareRows(a: MatrixRow, b: MatrixRow, key: MatrixSortKey, dir: 1 | -1): number {
    if (key === 'name') return dir * a.name.localeCompare(b.name);
    const av = a[key];
    const bv = b[key];
    if (av === null && bv === null) return 0;
    if (av === null) return 1;
    if (bv === null) return -1;
    return dir * (av - bv);
}

const MATRIX_COLUMNS: Array<{ key: MatrixSortKey; label: string; title: string }> = [
    { key: 'name', label: 'Entity', title: 'Tracked outlet, official, collective, or community' },
    { key: 'netTone', label: 'Net tone', title: "Positive minus negative share of the entity's own posts, -100..+100" },
    { key: 'botRate', label: 'Bot rate', title: 'Share of the entity\'s scored posts our detector flags as likely automated. News is not bot-scored.' },
    { key: 'posts', label: 'Posts', title: 'Posts scored for tone in this window' },
];

function CrossSignalMatrix({ rows }: { rows: MatrixRow[] }) {
    const [sortKey, setSortKey] = useState<MatrixSortKey>('posts');
    const [sortDir, setSortDir] = useState<1 | -1>(-1);
    const [query, setQuery] = useState('');

    const sorted = useMemo(() => {
        const q = query.trim().toLowerCase();
        const filtered = q ? rows.filter((r) => r.name.toLowerCase().includes(q)) : rows;
        return [...filtered].sort((a, b) => compareRows(a, b, sortKey, sortDir));
    }, [rows, query, sortKey, sortDir]);

    const onSort = (key: MatrixSortKey) => {
        if (key === sortKey) setSortDir((d) => (d === 1 ? -1 : 1));
        else { setSortKey(key); setSortDir(key === 'name' ? 1 : -1); }
    };

    const toneColor = (net: number) => (net > 10 ? COLORS.positive : net < -10 ? COLORS.negative : 'var(--neutral-600)');

    return (
        <Card
            title="Cross-signal matrix"
            subtitle="Tracked entities joined across the tone and bot-rate aggregations for the same window. An em dash means no data for that signal, not zero."
            headerActions={
                <MethodPopover
                    description={
                        'One row per tracked entity, joined by (kind, name) across the tone and bot '
                        + 'aggregations for the same window — both summarize sampled posts, not polls.'
                    }
                />
            }
        >
            <input
                type="search"
                className="input desk-search"
                placeholder="Search entities by name..."
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                aria-label="Search entities by name"
            />
            <div className="desk-table-wrap">
                <table className="table desk-matrix">
                    <thead>
                        <tr>
                            {MATRIX_COLUMNS.map((col) => (
                                <th key={col.key} className={col.key === 'name' ? '' : 'num'} title={col.title}>
                                    <button type="button" className="desk-sort-btn" onClick={() => onSort(col.key)}>
                                        {col.label}
                                        {sortKey === col.key && <span aria-hidden> {sortDir === -1 ? '▾' : '▴'}</span>}
                                    </button>
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody>
                        {sorted.map((row) => (
                            <tr key={row.id}>
                                <td>{row.name}</td>
                                <td className="num" style={row.netTone != null ? { color: toneColor(row.netTone) } : undefined}>
                                    {row.netTone != null ? formatPts(row.netTone) : '—'}
                                </td>
                                <td className="num">{row.botRate != null ? formatPct(row.botRate) : '—'}</td>
                                <td className="num">{formatCount(row.posts)}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </Card>
    );
}

function MoversBoard({ movers }: { movers: MoversResponse }) {
    const rows = movers.toneMovers;
    if (rows.length === 0 && !movers.topFavorabilityMover) return null;
    return (
        <Card title="Movers board" subtitle="Biggest window-over-window shifts in net tone. Check the sample sizes — a big move on few posts is noise, not news.">
            <RangeCaption range={movers.currentRange} />
            <MoversTicker data={movers} />
            {rows.length > 0 && (
                <div className="desk-table-wrap" style={{ marginTop: 'var(--space-3)' }}>
                    <table className="table">
                        <thead>
                            <tr><th>Entity</th><th className="num">Previous</th><th className="num">Now</th><th className="num">Shift</th><th className="num">Posts (prev → now)</th></tr>
                        </thead>
                        <tbody>
                            {rows.map((m) => (
                                <tr key={`${m.kind}:${m.entityKey}`}>
                                    <td>{m.displayName}</td>
                                    <td className="num">{formatPts(m.prevNet)}</td>
                                    <td className="num">{formatPts(m.currentNet)}</td>
                                    <td className="num" style={{ color: m.deltaPts > 0.5 ? COLORS.positive : m.deltaPts < -0.5 ? COLORS.negative : undefined, fontWeight: 700 }}>
                                        {formatPts(m.deltaPts)}
                                    </td>
                                    <td className="num">{formatCount(m.prevVolume)} → {formatCount(m.currentVolume)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </Card>
    );
}

function SmallMultiples({ narratives }: { narratives: NarrativesResponse | null }) {
    const topStories = [...(narratives?.narratives ?? [])]
        .sort((a, b) => b.docCount - a.docCount)
        .slice(0, 6)
        .filter((n) => n.timeline.length >= 2);
    if (topStories.length === 0) return null;
    return (
        <Card title="Small multiples" subtitle="Top stories' daily volume curves, side by side.">
            <div className="desk-multiples">
                {topStories.map((n) => (
                    <div key={n.narrativeId} className="desk-multiple" title={n.anchorClaimText ?? undefined}>
                        <div className="desk-multiple-label">{n.anchorClaimText || '(unnamed)'}</div>
                        <Sparkline
                            data={n.timeline.map((t) => ({ date: t.day, value: t.docCount }))}
                            height={48}
                            color="var(--neutral-600)"
                        />
                    </div>
                ))}
            </div>
        </Card>
    );
}

function PipelineHealthCard({ status }: { status: SnapshotStatusResponse | null }) {
    const run = status?.pipelineRun;
    if (!run) return null;
    return (
        <Card title="Pipeline health" subtitle="The most recent recorded pipeline run.">
            <div className="desk-table-wrap">
                <table className="table">
                    <tbody>
                        <tr><td>Status</td><td className="num" style={{ textTransform: 'capitalize' }}>{run.status}</td></tr>
                        <tr><td>Started</td><td className="num">{formatRefreshedAgo(run.startedAt)}</td></tr>
                        <tr><td>Completed</td><td className="num">{run.completedAt ? formatRefreshedAgo(run.completedAt) : '—'}</td></tr>
                    </tbody>
                </table>
            </div>
            {run.stageSummary && (
                <pre className="doc-analysis-fields" style={{ fontSize: 'var(--text-xs)', overflow: 'auto' }}>
                    {JSON.stringify(run.stageSummary, null, 2)}
                </pre>
            )}
        </Card>
    );
}

function HumanReviewCard({ evalAccuracy }: { evalAccuracy: EvalAccuracy | null }) {
    const tasks = evalAccuracy?.perTask ?? [];
    if (tasks.length === 0) return null;
    return (
        <Card title="Human review agreement" subtitle="How often human reviewers marked each task's outputs correct.">
            <div className="desk-table-wrap">
                <table className="table">
                    <thead><tr><th>Task</th><th className="num">Reviewed</th><th className="num">Agreement</th></tr></thead>
                    <tbody>
                        {tasks.map((t) => (
                            <tr key={t.taskType}>
                                <td style={{ textTransform: 'capitalize' }}>{t.taskType.replace(/_/g, ' ')}</td>
                                <td className="num">{formatCount(t.scored)}</td>
                                <td className="num">
                                    {t.accuracyPct != null && !t.lowSample ? formatPct(t.accuracyPct, { decimals: 0 }) : 'low sample'}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </Card>
    );
}

interface DataDeskProps {
    filters: Filters;
}

function DataDesk({ filters }: DataDeskProps) {
    const window = filters.timeRange;
    // GET /movers rejects window='all' -- fall back to a bounded window when
    // the reader has picked 'All time' elsewhere on the page.
    const moversWindow: MoversWindow = window === 'all' ? '90d' : window;

    const sentimentFetch = useFetch<SentimentPanelResponse>(
        () => fetchSentiment(window), [window], `sentiment:${window}`,
    );
    const botsFetch = useFetch<BotActivityResponse>(
        () => fetchBotActivity(window), [window], `bot-activity:${window}`,
    );
    const narrativesFetch = useFetch<NarrativesResponse>(
        () => fetchNarratives(window), [window], `narratives:${window}`,
    );
    const propagandaFetch = useFetch<PropagandaOverview>(
        () => fetchPropaganda(window), [window], `propaganda:${window}`,
    );
    const moversFetch = useFetch<MoversResponse>(
        () => fetchMovers(moversWindow), [moversWindow], `movers:${moversWindow}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatusResponse>(
        () => fetchSnapshotStatus(), [], 'snapshot-status',
    );
    const { data: evalAccuracy } = useFetch<EvalAccuracy>(
        () => fetchEvalAccuracy(), [], 'eval-accuracy',
    );

    const matrix = useMemo(
        () => buildMatrix(sentimentFetch.data, botsFetch.data),
        [sentimentFetch.data, botsFetch.data],
    );

    const anyLoading = sentimentFetch.loading || botsFetch.loading || narrativesFetch.loading || propagandaFetch.loading;
    const allFailed = sentimentFetch.error && botsFetch.error && narrativesFetch.error && propagandaFetch.error;

    if (allFailed) return <ErrorState message={sentimentFetch.error!.message} onRetry={sentimentFetch.refetch} />;
    if (anyLoading && matrix.length === 0) {
        return <div className="flex flex-col gap-4"><LoadingCard /><div className="grid-2"><LoadingCard /><LoadingCard /></div></div>;
    }
    if (matrix.length === 0 && !moversFetch.data) {
        return <EmptyState title="No signals available for this window yet." />;
    }

    const hasSnapshots = !!snapshotStatus?.pipelineRun;
    const hasReview = (evalAccuracy?.perTask?.length ?? 0) > 0;
    const hasLeftColumn = hasSnapshots || hasReview;

    return (
        <div className="dashboard-grid">
            {matrix.length > 0 && <div className="col-span-12"><CrossSignalMatrix rows={matrix} /></div>}
            {hasLeftColumn && (
                <div className="col-span-5">
                    {hasSnapshots && <PipelineHealthCard status={snapshotStatus} />}
                    {hasReview && <HumanReviewCard evalAccuracy={evalAccuracy} />}
                </div>
            )}
            <div className={hasLeftColumn ? 'col-span-7' : 'col-span-12'}>
                <SmallMultiples narratives={narrativesFetch.data} />
                {moversFetch.data && <MoversBoard movers={moversFetch.data} />}
            </div>
        </div>
    );
}

export default DataDesk;
