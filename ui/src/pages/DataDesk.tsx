import { useMemo, useState } from 'react';
import {
    Card, EmptyState, ErrorState, LoadingCard, MethodPopover, MoversTicker,
} from '../components/common';
import { Sparkline } from '../components/charts';
import {
    fetchBotActivity, fetchEvalAccuracy, fetchMovers, fetchNarratives,
    fetchPropaganda, fetchSentiment, fetchSnapshotStatus,
    type SnapshotStatus,
} from '../services/api';
import { formatRefreshedAgo } from '../services/freshness';
import { formatCount, formatPct, formatPts } from '../services/format';
import { transformPublicSentiment } from '../services/transformers';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import type {
    BotData, EvalAccuracy, Filters, MoversResult, NarrativeSummary,
    PropagandaOverview, PublicSentimentData,
} from '../types';

// --------------------------------------------------------------------------- //
//  Data Desk — the numbers-forward page. The reader-first tabs lead with      //
//  one takeaway each; this one puts every signal side-by-side: a sortable     //
//  cross-signal entity matrix, the full movers board, small multiples, and    //
//  the pipeline's own health readouts. Same data, terminal density.          //
// --------------------------------------------------------------------------- //

interface MatrixRow {
    id: string;
    name: string;
    group: 'News' | 'Officials' | 'Public';
    netTone: number | null;
    posts: number | null;
    flaggedRate: number | null;
    botRate: number | null;
    stories: number;
}

type MatrixSortKey = 'name' | 'netTone' | 'posts' | 'flaggedRate' | 'botRate' | 'stories';

/**
 * Client-side join across the four snapshot payloads on `kind:key` — the
 * entity keys are shared across aggregators by construction (same registry).
 * Catch-all buckets are excluded: the matrix reads "per tracked entity".
 */
function buildMatrix(
    sentiment: PublicSentimentData | null,
    propaganda: PropagandaOverview | null,
    bots: BotData | null,
    narratives: NarrativeSummary[] | null,
): MatrixRow[] {
    const rows = new Map<string, MatrixRow>();

    const ensure = (
        kind: string, key: string, name: string, group: MatrixRow['group'],
    ): MatrixRow | null => {
        if (kind === 'catch_all') return null;
        const id = `${kind}:${key}`;
        let row = rows.get(id);
        if (!row) {
            row = {
                id, name, group,
                netTone: null, posts: null, flaggedRate: null, botRate: null, stories: 0,
            };
            rows.set(id, row);
        }
        return row;
    };

    const sentimentTiers: Array<[PublicSentimentData['byNewsOutlet'], MatrixRow['group']]> = [
        [sentiment?.byNewsOutlet, 'News'],
        [sentiment?.byOfficial, 'Officials'],
        [sentiment?.byGeneralPublic, 'Public'],
    ];
    for (const [items, group] of sentimentTiers) {
        for (const it of items ?? []) {
            const row = ensure(it.kind, it.key, it.entityProfile.displayName, group);
            if (!row || it.volume === 0) continue;
            row.netTone = it.netScore;
            row.posts = it.volume;
        }
    }

    const propagandaTiers: Array<[PropagandaOverview['by_news_outlet'], MatrixRow['group']]> = [
        [propaganda?.by_news_outlet, 'News'],
        [propaganda?.by_official, 'Officials'],
        [propaganda?.by_general_public, 'Public'],
    ];
    for (const [items, group] of propagandaTiers) {
        for (const it of items ?? []) {
            const row = ensure(it.kind, it.key, it.entity_profile.displayName, group);
            if (!row || it.total_docs === 0) continue;
            row.flaggedRate = it.flagged_rate_pct;
        }
    }

    const botTiers: Array<[BotData['overview']['by_news_outlet'], MatrixRow['group']]> = [
        [bots?.overview.by_news_outlet, 'News'],
        [bots?.overview.by_official, 'Officials'],
        [bots?.overview.by_general_public, 'Public'],
    ];
    for (const [items, group] of botTiers) {
        for (const it of items ?? []) {
            const row = ensure(it.kind, it.key, it.entity_profile.displayName, group);
            if (!row || it.total_docs === 0) continue;
            row.botRate = it.bot_rate_pct;
        }
    }

    for (const n of narratives ?? []) {
        const p = n.first_seen_entity_profile;
        if (!p) continue;
        const group: MatrixRow['group'] = n.first_seen_tier_group === 'news'
            ? 'News'
            : n.first_seen_tier_group === 'officials' ? 'Officials' : 'Public';
        const row = ensure(p.kind, p.key, p.displayName, group);
        if (row) row.stories += 1;
    }

    return Array.from(rows.values());
}

function compareRows(a: MatrixRow, b: MatrixRow, key: MatrixSortKey, dir: 1 | -1): number {
    if (key === 'name') return dir * a.name.localeCompare(b.name);
    const av = a[key];
    const bv = b[key];
    // Nulls (no data) always sort to the bottom regardless of direction.
    if (av === null && bv === null) return 0;
    if (av === null) return 1;
    if (bv === null) return -1;
    return dir * (av - bv);
}

const MATRIX_COLUMNS: Array<{ key: MatrixSortKey; label: string; title: string }> = [
    { key: 'name', label: 'Entity', title: 'Tracked outlet, official, or community' },
    { key: 'netTone', label: 'Net tone', title: 'Positive minus negative share of their posts, -100..+100' },
    { key: 'flaggedRate', label: 'Propaganda', title: 'Share of their scored posts flagged for persuasion techniques' },
    { key: 'botRate', label: 'Bot rate', title: 'Share of their scored posts our detector flags as likely automated' },
    { key: 'stories', label: 'Stories', title: 'Recurring claims first seen at this source in our sample' },
    { key: 'posts', label: 'Posts', title: 'Posts scored for tone in this window' },
];

function CrossSignalMatrix({ rows }: { rows: MatrixRow[] }) {
    const [sortKey, setSortKey] = useState<MatrixSortKey>('posts');
    const [sortDir, setSortDir] = useState<1 | -1>(-1);

    const sorted = useMemo(
        () => [...rows].sort((a, b) => compareRows(a, b, sortKey, sortDir)),
        [rows, sortKey, sortDir],
    );

    const onSort = (key: MatrixSortKey) => {
        if (key === sortKey) {
            setSortDir((d) => (d === 1 ? -1 : 1));
        } else {
            setSortKey(key);
            setSortDir(key === 'name' ? 1 : -1);
        }
    };

    const toneColor = (net: number) => net > 10
        ? COLORS.positive
        : net < -10 ? COLORS.negative : 'var(--neutral-600)';

    return (
        <Card
            title="Cross-signal matrix"
            subtitle="Every tracked entity across all four signals in the selected window. Click a column to sort; an em dash means no data for that signal, not zero."
            headerActions={
                <MethodPopover
                    description={
                        'One row per tracked entity, joined across the tone, propaganda, '
                        + 'bot, and narrative aggregations for the same window. All four '
                        + 'summarize sampled posts — samples, not polls. Entities we track '
                        + 'but did not sample in this window are omitted.'
                    }
                />
            }
        >
            <div className="desk-table-wrap">
                <table className="table desk-matrix">
                    <thead>
                        <tr>
                            <th className="desk-matrix-group">Group</th>
                            {MATRIX_COLUMNS.map((col) => (
                                <th
                                    key={col.key}
                                    className={col.key === 'name' ? '' : 'num'}
                                    title={col.title}
                                >
                                    <button
                                        type="button"
                                        className="desk-sort-btn"
                                        onClick={() => onSort(col.key)}
                                        aria-label={`Sort by ${col.label}`}
                                    >
                                        {col.label}
                                        {sortKey === col.key && (
                                            <span aria-hidden> {sortDir === -1 ? '▾' : '▴'}</span>
                                        )}
                                    </button>
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody>
                        {sorted.map((row) => (
                            <tr key={row.id}>
                                <td className="desk-matrix-group">{row.group}</td>
                                <td>{row.name}</td>
                                <td className="num" style={row.netTone != null ? { color: toneColor(row.netTone) } : undefined}>
                                    {row.netTone != null ? formatPts(row.netTone) : '—'}
                                </td>
                                <td className="num">{row.flaggedRate != null ? formatPct(row.flaggedRate) : '—'}</td>
                                <td className="num">{row.botRate != null ? formatPct(row.botRate) : '—'}</td>
                                <td className="num">{row.stories > 0 ? row.stories : '—'}</td>
                                <td className="num">{formatCount(row.posts)}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Movers board — the full window-over-window table behind the ticker.        //
// --------------------------------------------------------------------------- //

function MoversBoard({ movers }: { movers: MoversResult }) {
    const rows = movers.entity_movers;
    if (rows.length === 0 && !movers.favorability_mover) return null;
    return (
        <Card
            title="Movers board"
            subtitle="Biggest window-over-window shifts in net tone. Check the sample sizes — a big move on few posts is noise, not news."
        >
            <MoversTicker data={movers} />
            {rows.length > 0 && (
                <div className="desk-table-wrap" style={{ marginTop: 'var(--space-3)' }}>
                    <table className="table">
                        <thead>
                            <tr>
                                <th>Entity</th>
                                <th className="num">Previous</th>
                                <th className="num">Now</th>
                                <th className="num">Shift</th>
                                <th className="num">Posts (prev → now)</th>
                            </tr>
                        </thead>
                        <tbody>
                            {rows.map((m) => (
                                <tr key={`${m.kind}:${m.key}`}>
                                    <td>{m.displayName}</td>
                                    <td className="num">{formatPts(m.prev_net)}</td>
                                    <td className="num">{formatPts(m.current_net)}</td>
                                    <td
                                        className="num"
                                        style={{
                                            color: m.delta_pts > 0.5
                                                ? COLORS.positive
                                                : m.delta_pts < -0.5 ? COLORS.negative : undefined,
                                            fontWeight: 700,
                                        }}
                                    >
                                        {formatPts(m.delta_pts)}
                                    </td>
                                    <td className="num">
                                        {formatCount(m.prev_volume)} → {formatCount(m.current_volume)}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Small multiples — the page heroes at terminal density.                     //
// --------------------------------------------------------------------------- //

function SmallMultiples({
    sentiment, narratives,
}: {
    sentiment: PublicSentimentData | null;
    narratives: NarrativeSummary[] | null;
}) {
    const gopTrend = (sentiment?.gopTrend ?? []).filter((p) => Number.isFinite(p.value));
    const topStories = [...(narratives ?? [])]
        .sort((a, b) => b.supporting_doc_count - a.supporting_doc_count)
        .slice(0, 5)
        .filter((n) => n.timeline.length >= 2);
    if (gopTrend.length < 2 && topStories.length === 0) return null;

    return (
        <Card
            title="Small multiples"
            subtitle="The page heroes, side by side: daily GOP tone and the top stories' volume curves."
        >
            <div className="desk-multiples">
                {gopTrend.length >= 2 && (
                    <div className="desk-multiple">
                        <div className="desk-multiple-label">Tone toward GOP · daily</div>
                        <Sparkline
                            data={gopTrend}
                            height={48}
                            ariaLabel={`Daily GOP net tone, ${gopTrend.length} days`}
                        />
                    </div>
                )}
                {topStories.map((n) => (
                    <div key={n.narrative_id} className="desk-multiple">
                        <div className="desk-multiple-label" title={n.name}>{n.name}</div>
                        <Sparkline
                            data={n.timeline.map((t) => ({ date: t.date, value: t.count }))}
                            height={48}
                            color="var(--neutral-600)"
                            ariaLabel={`Daily volume for ${n.name}`}
                        />
                    </div>
                ))}
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Pipeline health — the audit ethos as a visible dashboard.                  //
// --------------------------------------------------------------------------- //

function PipelineHealth({
    status, evalAccuracy,
}: {
    status: SnapshotStatus | null;
    evalAccuracy: EvalAccuracy | null;
}) {
    const snapshots = status?.snapshots ?? [];
    const tasks = evalAccuracy?.perTask ?? [];
    if (snapshots.length === 0 && tasks.length === 0) return null;

    return (
        <div className="grid-2">
            {snapshots.length > 0 && (
                <Card
                    title="Snapshot freshness"
                    subtitle="When each cached aggregation was last rebuilt, and over how many documents."
                >
                    <div className="desk-table-wrap">
                        <table className="table">
                            <thead>
                                <tr>
                                    <th>Snapshot</th>
                                    <th className="num">Refreshed</th>
                                    <th className="num">Docs</th>
                                </tr>
                            </thead>
                            <tbody>
                                {snapshots.map((s) => (
                                    <tr key={s.key}>
                                        <td style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-xs)' }}>
                                            {s.key}
                                        </td>
                                        <td className="num">{formatRefreshedAgo(s.generated_at)}</td>
                                        <td className="num">{formatCount(s.doc_count)}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </Card>
            )}
            {tasks.length > 0 && (
                <Card
                    title="Human review agreement"
                    subtitle="How often human reviewers marked each classifier's outputs correct. Reviews cover a sample of outputs; accuracy is withheld below the minimum review count."
                >
                    <div className="desk-table-wrap">
                        <table className="table">
                            <thead>
                                <tr>
                                    <th>Classifier</th>
                                    <th className="num">Reviewed</th>
                                    <th className="num">Agreement</th>
                                </tr>
                            </thead>
                            <tbody>
                                {tasks.map((t) => (
                                    <tr key={t.taskType}>
                                        <td style={{ textTransform: 'capitalize' }}>
                                            {t.taskType.replace(/_/g, ' ')}
                                        </td>
                                        <td className="num">{formatCount(t.scored)}</td>
                                        <td className="num">
                                            {t.accuracyPct != null && !t.lowSample
                                                ? formatPct(t.accuracyPct, { decimals: 0 })
                                                : 'low sample'}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </Card>
            )}
        </div>
    );
}

// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

interface DataDeskProps {
    filters: Filters;
}

function DataDesk({ filters }: DataDeskProps) {
    const window = filters.timeRange;
    const sentimentFetch = useFetch<PublicSentimentData>(
        async () => transformPublicSentiment(await fetchSentiment(window)),
        [window],
        `sentiment:${window}`,
    );
    const propagandaFetch = useFetch<PropagandaOverview>(
        () => fetchPropaganda(window), [window], `propaganda:${window}`,
    );
    const botsFetch = useFetch<BotData>(
        () => fetchBotActivity(window), [window], `bot-activity:${window}`,
    );
    const narrativesFetch = useFetch<NarrativeSummary[]>(
        () => fetchNarratives(window), [window], `narratives:${window}`,
    );
    const moversFetch = useFetch<MoversResult>(
        () => fetchMovers(window), [window], `movers:${window}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatus>(
        () => fetchSnapshotStatus(), [], 'snapshot-status',
    );
    const { data: evalAccuracy } = useFetch<EvalAccuracy>(
        () => fetchEvalAccuracy(), [], 'eval-accuracy',
    );

    const matrix = useMemo(
        () => buildMatrix(
            sentimentFetch.data, propagandaFetch.data, botsFetch.data, narrativesFetch.data,
        ),
        [sentimentFetch.data, propagandaFetch.data, botsFetch.data, narrativesFetch.data],
    );

    const anyLoading = sentimentFetch.loading || propagandaFetch.loading
        || botsFetch.loading || narrativesFetch.loading;
    const allFailed = sentimentFetch.error && propagandaFetch.error
        && botsFetch.error && narrativesFetch.error;

    if (allFailed) {
        return <ErrorState message={sentimentFetch.error!.message} onRetry={sentimentFetch.refetch} />;
    }
    if (anyLoading && matrix.length === 0) {
        return (
            <div className="flex flex-col gap-4">
                <LoadingCard />
                <div className="grid-2">
                    <LoadingCard />
                    <LoadingCard />
                </div>
            </div>
        );
    }
    if (matrix.length === 0 && !moversFetch.data) {
        return <EmptyState title="No signals available for this window yet." />;
    }

    return (
        <div className="dashboard-grid">
            {matrix.length > 0 && (
                <div className="col-span-12">
                    <CrossSignalMatrix rows={matrix} />
                </div>
            )}
            {moversFetch.data && (
                <div className="col-span-12">
                    <MoversBoard movers={moversFetch.data} />
                </div>
            )}
            <div className="col-span-12">
                <SmallMultiples
                    sentiment={sentimentFetch.data}
                    narratives={narrativesFetch.data}
                />
            </div>
            <div className="col-span-12">
                <PipelineHealth status={snapshotStatus} evalAccuracy={evalAccuracy} />
            </div>
        </div>
    );
}

export default DataDesk;
