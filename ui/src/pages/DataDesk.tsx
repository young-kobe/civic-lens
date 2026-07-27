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
    BotActivityResponse, BotEntityItem, EntitySentimentItem, EvalAccuracy, Filters,
    MoversResponse, NarrativeSummary, NarrativesResponse, PropagandaEntityItem,
    PropagandaOverview, SentimentPanelResponse, SnapshotStatusResponse,
} from '../types';

// --------------------------------------------------------------------------- //
//  Data Desk — the numbers-forward page. The reader-first tabs lead with      //
//  one takeaway each; this one puts every signal side-by-side: a sortable     //
//  cross-signal entity matrix, the full movers board, small multiples, and    //
//  the pipeline's own health readouts. Same data, terminal density.          //
//                                                                             //
//  Restores the pre-cutover-main matrix's five data columns (net tone,        //
//  propaganda flagged rate, bot rate, stories, posts) now that /sentiment,    //
//  /propaganda, and /bot-activity all carry the same server-tiered           //
//  byNewsOutlet/byOfficial/byGeneralPublic shape (key/kind/entityProfile) --  //
//  see docs/todos/ui-feature-restoration.md. The join key is `kind:key` per   //
//  entity, exactly as the tag-era matrix joined it; the "Group" column comes  //
//  from which of the three tiered lists a row appeared in (server-tier        //
//  grouping), not a client-side guess from `kind`.                           //
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
 * Client-side join across the four live payloads on `kind:key` — the entity
 * keys are shared across aggregators by construction (same registry).
 * Catch-all buckets are excluded: the matrix reads "per tracked entity".
 */
function buildMatrix(
    sentiment: SentimentPanelResponse | null,
    propaganda: PropagandaOverview | null,
    bots: BotActivityResponse | null,
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

    const sentimentTiers: Array<[EntitySentimentItem[] | undefined, MatrixRow['group']]> = [
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

    const propagandaTiers: Array<[PropagandaEntityItem[] | undefined, MatrixRow['group']]> = [
        [propaganda?.byNewsOutlet, 'News'],
        [propaganda?.byOfficial, 'Officials'],
        [propaganda?.byGeneralPublic, 'Public'],
    ];
    for (const [items, group] of propagandaTiers) {
        for (const it of items ?? []) {
            const row = ensure(it.kind, it.key, it.entityProfile.displayName, group);
            if (!row || it.totalDocs === 0) continue;
            row.flaggedRate = it.flaggedRatePct;
        }
    }

    // News is not bot-scored (articles are not accounts) — news rows keep
    // botRate null and render an em dash in the Bot rate column.
    const botTiers: Array<[BotEntityItem[] | undefined, MatrixRow['group']]> = [
        [bots?.byOfficial, 'Officials'],
        [bots?.byGeneralPublic, 'Public'],
    ];
    for (const [items, group] of botTiers) {
        for (const it of items ?? []) {
            const row = ensure(it.kind, it.key, it.entityProfile.displayName, group);
            if (!row || it.totalDocs === 0) continue;
            row.botRate = it.botRatePct;
        }
    }

    for (const n of narratives ?? []) {
        const p = n.firstSeenEntityProfile;
        if (!p) continue;
        const group: MatrixRow['group'] = n.firstSeenTierGroup === 'news'
            ? 'News'
            : n.firstSeenTierGroup === 'officials' ? 'Officials' : 'Public';
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
    { key: 'netTone', label: 'Net tone', title: 'Positive minus negative share of their posts, from -100 to +100' },
    { key: 'flaggedRate', label: 'Propaganda', title: 'Share of their scored posts flagged for persuasion techniques' },
    { key: 'botRate', label: 'Bot rate', title: 'Share of their scored posts our detector flags as likely automated. News is not bot-scored (articles are not accounts).' },
    { key: 'stories', label: 'Stories', title: 'Recurring claims first seen at this source in our sample' },
    { key: 'posts', label: 'Posts', title: 'Posts scored for tone in this window' },
];

function CrossSignalMatrix({ rows }: { rows: MatrixRow[] }) {
    const [sortKey, setSortKey] = useState<MatrixSortKey>('posts');
    const [sortDir, setSortDir] = useState<1 | -1>(-1);
    const [query, setQuery] = useState('');

    const sorted = useMemo(() => {
        const q = query.trim().toLowerCase();
        const filtered = q
            ? rows.filter((r) => r.name.toLowerCase().includes(q))
            : rows;
        return [...filtered].sort((a, b) => compareRows(a, b, sortKey, sortDir));
    }, [rows, query, sortKey, sortDir]);

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
            subtitle="Every tracked entity, with its tone, propaganda, and bot-detection numbers side by side. Click a column to sort; an em dash means no data for that signal, not zero."
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
            <input
                type="search"
                className="input desk-search"
                placeholder="Search entities by name..."
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                aria-label="Search entities by name"
            />
            {sorted.length === 0 && query.trim() !== '' && (
                <p className="text-sm text-muted">
                    No tracked entity matches "{query.trim()}" in this window.
                </p>
            )}
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

function MoversBoard({ movers }: { movers: MoversResponse }) {
    const rows = movers.toneMovers;
    if (rows.length === 0 && !movers.topFavorabilityMover) return null;
    return (
        <Card
            title="Movers board"
            subtitle="Biggest shifts in net tone compared with the previous period. Check the sample sizes — a big move on few posts is noise, not news."
        >
            <RangeCaption range={movers.currentRange} />
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
                                <tr key={`${m.kind}:${m.entityKey}`}>
                                    <td>{m.displayName}</td>
                                    <td className="num">{formatPts(m.prevNet)}</td>
                                    <td className="num">{formatPts(m.currentNet)}</td>
                                    <td
                                        className="num"
                                        style={{
                                            color: m.deltaPts > 0.5
                                                ? COLORS.positive
                                                : m.deltaPts < -0.5 ? COLORS.negative : undefined,
                                            fontWeight: 700,
                                        }}
                                    >
                                        {formatPts(m.deltaPts)}
                                    </td>
                                    <td className="num">
                                        {formatCount(m.prevVolume)} → {formatCount(m.currentVolume)}
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
//                                                                             //
//  The pre-cutover panel led with a daily GOP-tone sparkline                  //
//  (`sentiment.gopTrend`), which the strictly-live contract doesn't publish   //
//  (no daily series scoped to a single party). Net tone by source type       //
//  (byPlatform) is the closest existing series in the current payload --      //
//  categorical rather than daily, but a real, honestly-labeled reading        //
//  rather than an empty slot.                                                //
// --------------------------------------------------------------------------- //

function SmallMultiples({
    sentiment, narratives,
}: {
    sentiment: SentimentPanelResponse | null;
    narratives: NarrativesResponse | null;
}) {
    const platformTone = (sentiment?.byPlatform ?? [])
        .filter((p) => p.netScore != null)
        .map((p) => ({ date: p.platform, value: p.netScore as number }));
    const topStories = [...(narratives?.narratives ?? [])]
        .sort((a, b) => b.docCount - a.docCount)
        .slice(0, 5)
        .filter((n) => n.timeline.length >= 2);
    if (platformTone.length < 2 && topStories.length === 0) return null;

    return (
        <Card
            title="Trend snapshots"
            subtitle="Small charts side by side: net tone by source type and the top stories' volume curves."
        >
            <div className="desk-multiples">
                {platformTone.length >= 2 && (
                    <div className="desk-multiple">
                        <div className="desk-multiple-label">Net tone by source type</div>
                        <Sparkline
                            data={platformTone}
                            height={48}
                            showXAxis
                            ariaLabel={`Net tone by source type: ${platformTone.map((p) => `${p.date} ${p.value}`).join(', ')}`}
                        />
                    </div>
                )}
                {topStories.map((n) => (
                    <div key={n.narrativeId} className="desk-multiple" title={n.anchorClaimText ?? undefined}>
                        <div className="desk-multiple-label">{n.anchorClaimText || '(unnamed)'}</div>
                        <Sparkline
                            data={n.timeline.map((t) => ({ date: t.day, value: t.docCount }))}
                            height={48}
                            color="var(--neutral-600)"
                            ariaLabel={`Daily volume for ${n.anchorClaimText || '(unnamed)'}`}
                        />
                    </div>
                ))}
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Pipeline health — the audit ethos as a visible dashboard.                  //
//                                                                             //
//  Restored under the old "Snapshot freshness" card frame (name and          //
//  desk-table-scroll geometry), fed by the single GET /snapshot-status       //
//  pipeline run rather than the retired per-snapshot cache-metadata table --  //
//  the strictly-live API has one pipeline run, not one row per cached        //
//  aggregation, so this is a one-row table rather than the old ~21-row one.  //
// --------------------------------------------------------------------------- //

function SnapshotFreshnessCard({ status }: { status: SnapshotStatusResponse | null }) {
    const run = status?.pipelineRun;
    if (!run) return null;
    return (
        <Card
            title="Snapshot freshness"
            subtitle={`Data last refreshed ${formatRefreshedAgo(run.completedAt ?? run.startedAt)}.`}
        >
            <div className="desk-table-wrap desk-table-scroll">
                <table className="table">
                    <thead>
                        <tr>
                            <th>Snapshot</th>
                            <th className="num">Refreshed</th>
                            <th className="num">Status</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr>
                            <td style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-xs)' }}>
                                pipeline run #{run.pipelineRunId}
                            </td>
                            <td className="num">
                                {formatRefreshedAgo(run.completedAt ?? run.startedAt)}
                            </td>
                            <td className="num" style={{ textTransform: 'capitalize' }}>{run.status}</td>
                        </tr>
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

// Reader-facing names for analysis.task (data/pg-migrations/0001_north_star.sql:
// bot, text, targets, propaganda, claims, citations, account_tier). Built
// explicitly rather than title-casing the raw enum value, which reads as
// internal shorthand ("targets", "account_tier") to a non-technical reader.
const TASK_LABEL: Record<string, string> = {
    bot: 'Bot detection',
    text: 'Tone',
    targets: 'Target stance',
    propaganda: 'Propaganda',
    claims: 'Claim extraction',
    citations: 'Citations',
    account_tier: 'Account tier',
};

function taskLabel(taskType: string): string {
    return TASK_LABEL[taskType] ?? taskType.replace(/_/g, ' ');
}

function HumanReviewCard({ evalAccuracy }: { evalAccuracy: EvalAccuracy | null }) {
    const tasks = evalAccuracy?.perTask ?? [];
    if (tasks.length === 0) return null;
    return (
        <Card
            title="Human review agreement"
            subtitle="How often human reviewers marked each analysis type's outputs correct. Reviews cover a sample of outputs; accuracy is withheld below the minimum review count."
        >
            <div className="desk-table-wrap">
                <table className="table">
                    <thead>
                        <tr>
                            <th>Analysis type</th>
                            <th className="num">Reviewed</th>
                            <th className="num">Agreement</th>
                        </tr>
                    </thead>
                    <tbody>
                        {tasks.map((t) => (
                            <tr key={t.taskType}>
                                <td>{taskLabel(t.taskType)}</td>
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
    // GET /movers rejects window='all' -- fall back to a bounded window when
    // the reader has picked 'All time' elsewhere on the page.
    const moversWindow: MoversWindow = window === 'all' ? '90d' : window;

    const sentimentFetch = useFetch<SentimentPanelResponse>(
        () => fetchSentiment(window), [window], `sentiment:${window}`,
    );
    const propagandaFetch = useFetch<PropagandaOverview>(
        () => fetchPropaganda(window), [window], `propaganda:${window}`,
    );
    const botsFetch = useFetch<BotActivityResponse>(
        () => fetchBotActivity(window), [window], `bot-activity:${window}`,
    );
    const narrativesFetch = useFetch<NarrativesResponse>(
        () => fetchNarratives(window), [window], `narratives:${window}`,
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
        () => buildMatrix(
            sentimentFetch.data, propagandaFetch.data, botsFetch.data,
            narrativesFetch.data?.narratives ?? null,
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

    const hasSnapshots = !!snapshotStatus?.pipelineRun;
    const hasReview = (evalAccuracy?.perTask?.length ?? 0) > 0;
    const hasLeftColumn = hasSnapshots || hasReview;

    return (
        <div className="dashboard-grid">
            {matrix.length > 0 && (
                <div className="col-span-12">
                    <CrossSignalMatrix rows={matrix} />
                </div>
            )}
            {/* Two-column module block. Left column stacks the audit tables
                (snapshot freshness + human review); right column stacks the
                small multiples with the Movers board directly beneath them —
                Movers fills the area that was empty below the multiples grid. */}
            {hasLeftColumn && (
                <div className="col-span-5">
                    {hasSnapshots && <SnapshotFreshnessCard status={snapshotStatus} />}
                    {hasReview && <HumanReviewCard evalAccuracy={evalAccuracy} />}
                </div>
            )}
            <div className={hasLeftColumn ? 'col-span-7' : 'col-span-12'}>
                <SmallMultiples
                    sentiment={sentimentFetch.data}
                    narratives={narrativesFetch.data}
                />
                {moversFetch.data && <MoversBoard movers={moversFetch.data} />}
            </div>
        </div>
    );
}

export default DataDesk;
