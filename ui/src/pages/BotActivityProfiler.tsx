import { useEffect, useState } from 'react';
import {
    Card, CollapsibleInfo, DefinitionChip, EmptyState, EntityHeader,
    EntityHubLinks, ErrorState,
    GlobalTicker,
    LoadingCard, MethodPopover, Modal, PostCardList,
    RankedEntityList, ThreeWayColumn, ThreeWayGrid,
    entityExternalUrl, entityLeanAccent, flaggedExampleToPostCard,
    parseEntityParam,
} from '../components/common';
import { deepLinkHref, useDeepLinkParam } from '../services/deepLink';
import { coordinationLevel } from '../services/glossary';
import { CoordinationEvidencePanel } from './bots/CoordinationEvidencePanel';
import type { ColumnSorter, TickerItem } from '../components/common';
import { fetchBotActivity, fetchSnapshotStatus, type SnapshotStatus } from '../services/api';
import { useFetch } from '../services/useFetch';
import { formatRefreshedAgo, getSnapshotTimestamp } from '../services/freshness';
import { formatPct } from '../services/format';
import { COLORS } from '../theme';
import type {
    BehavioralSignals, BotData, BotEntityItem, BotOverview, ConfidenceLevel,
    CoordinationStats, Filters, NarrativeAmplification,
} from '../types';

// --------------------------------------------------------------------------- //
//  Amplification by tier — three-way entity grid                              //
// --------------------------------------------------------------------------- //

function botRateColor(ratePct: number): string {
    return ratePct > 10
        ? COLORS.negative
        : ratePct > 3
            ? COLORS.warning
            : 'var(--neutral-700)';
}

function BotEntityModal({ item, onClose }: { item: BotEntityItem; onClose: () => void }) {
    const profile = item.entity_profile;
    const rateColor = botRateColor(item.bot_rate_pct);
    const sourceUrl = entityExternalUrl(profile);

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle="Bot-detection drill-down"
            accentColor={entityLeanAccent(profile)}
        >
            <EntityHeader profile={profile} />
            <div className="entity-modal-stats">
                <div>
                    <div
                        className="eyebrow"
                        title="Share of this source's scored posts our detector flags as likely automated. A lead, not a verdict."
                    >
                        Suspected bot rate
                    </div>
                    <div className="metric-value" style={{ color: rateColor }}>
                        {item.total_docs > 0 ? formatPct(item.bot_rate_pct) : '—'}
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Flagged posts</div>
                    <div className="metric-value">{item.bot_docs.toLocaleString()}</div>
                </div>
                <div>
                    <div className="eyebrow">Posts scanned</div>
                    <div className="metric-value">{item.total_docs.toLocaleString()}</div>
                </div>
            </div>

            <h3 className="card-title mt-4 mb-2">Flagged posts from this source</h3>
            <PostCardList
                posts={(item.samples ?? []).map(flaggedExampleToPostCard)}
                sampleNote="The highest-confidence flagged posts from this source — a sample, not every flag."
                emptyNote={item.bot_docs > 0
                    ? 'Flagged posts exist but their excerpts are not in this snapshot yet — they will appear on the next data refresh.'
                    : 'No posts from this source were flagged in this window.'}
            />
            <div className="card-note mt-4">
                Bot flags are probabilistic leads, not verdicts. Every excerpt links
                back to the original post so you can judge it yourself.
            </div>
            {sourceUrl && (
                <div className="mt-3">
                    <a href={sourceUrl} target="_blank" rel="noreferrer" className="example-row-link">
                        Visit {profile.displayName} ↗
                    </a>
                </div>
            )}
            <EntityHubLinks profile={profile} currentTab="bots" />
        </Modal>
    );
}

const BOT_SORTERS: ColumnSorter<BotEntityItem>[] = [
    { label: 'bot rate', compare: (a, b) => b.bot_rate_pct - a.bot_rate_pct },
    { label: 'posts scanned', compare: (a, b) => b.total_docs - a.total_docs },
    { label: 'name', compare: (a, b) => a.entity_profile.displayName.localeCompare(b.entity_profile.displayName) },
];

function BotThreeWayGrid({
    overview, onOpen,
}: {
    overview: BotOverview;
    onOpen: (item: BotEntityItem) => void;
}) {
    // Always render the three-way frame when we have an overview, even if
    // individual tiers are empty — per-column empty copy is more honest
    // ("no official X posts scored") than hiding the grid entirely.
    const ranked = (label: string) => (items: BotEntityItem[]) => (
        <RankedEntityList
            items={items.map((it) => ({
                profile: it.entity_profile,
                rateValue: it.total_docs > 0 ? formatPct(it.bot_rate_pct) : '—',
                ratePct: it.bot_rate_pct,
                rateColor: botRateColor(it.bot_rate_pct),
                detail: `${it.bot_docs.toLocaleString()} of ${it.total_docs.toLocaleString()} flagged`,
                onClick: () => onOpen(it),
            }))}
            ariaLabel={label}
        />
    );

    return (
        <ThreeWayGrid>
                <ThreeWayColumn
                    header="The News"
                    byline="Outlets ranked by the share of their scanned articles our detector flags as likely automated."
                    empty="No news posts scored for bot detection."
                    items={overview.by_news_outlet ?? []}
                    renderItems={ranked('News outlets by suspected bot rate')}
                    sorters={BOT_SORTERS}
                />
                <ThreeWayColumn
                    header="Politicians & Officials"
                    byline="Tracked officeholders on X, ranked by the share of their X posts our detector flags as likely automated."
                    empty="No official X posts scored for bot detection."
                    items={overview.by_official ?? []}
                    renderItems={ranked('Officials by suspected bot rate')}
                    sorters={BOT_SORTERS}
                />
                <ThreeWayColumn
                    header="The Public"
                    byline="Political subreddits, plus X users we don't track individually, ranked by the share of their posts our detector flags as likely automated."
                    empty="No public social posts scored for bot detection."
                    items={overview.by_general_public ?? []}
                    renderItems={ranked('Public sources by suspected bot rate')}
                    sorters={BOT_SORTERS}
                />
        </ThreeWayGrid>
    );
}


/**
 * Strip the "www." prefix from domain clusters so the banner reads like a
 * source name, not a URL fragment.
 */
function friendlyCluster(cluster: string): string {
    return cluster.replace(/^www\./i, '');
}

/**
 * True for narrative/indicator labels that are actually raw signal
 * artifacts — `account_age=None days`, `followers=0`, `field=null`,
 * trailing `=`, etc. The backend bot detector sanitizes these at write
 * time (`_sanitize_llm_indicators` in `analysis/src/engine/bot.py`),
 * but historical `ai_outputs` rows written before that fix landed can
 * surface in the 24h/7d/30d aggregations until they age out of the
 * window. Filtering on display keeps readers from seeing a leaked
 * internal field name while the snapshot cache rebuilds.
 */
function isNoiseLabel(label: string | null | undefined): boolean {
    if (!label) return true;
    const text = label.trim();
    if (!text) return true;
    if (/=\s*(None|null|undefined|0)\b/i.test(text)) return true;
    if (/=\s*$/.test(text)) return true;
    return false;
}

/** Headline derived from top flagged cluster + automation rate. */
function readsAsToday(data: BotData): string {
    const rate = data.overview.suspectedAutomationRate;
    const topCluster = data.overview.topClusters[0];
    // Prefer the first non-noise narrative so we don't headline the banner
    // with a leaked signal-field name from a pre-sanitization row.
    const topNarrative = data.narrativeAmplification.find(
        (n) => !isNoiseLabel(n.narrative),
    );
    const parts: string[] = [];
    const ratePct = formatPct(rate, { decimals: 0 });
    if (rate > 10) {
        parts.push(`A high share of the posts we scanned look automated — roughly ${ratePct}.`);
    } else if (rate > 3) {
        parts.push(`Some of the posts we scanned look automated — about ${ratePct}.`);
    } else {
        parts.push(`Most posts we scanned look like real people — only about ${ratePct} look automated.`);
    }
    if (topCluster) {
        parts.push(`A lot of those suspect posts are pointing back at ${friendlyCluster(topCluster)}.`);
    }
    if (topNarrative) {
        const count = topNarrative.suspectedBotVolume.toLocaleString();
        parts.push(`${count} of them are amplifying the same narrative: "${topNarrative.narrative}".`);
    }
    return parts.join(' ');
}

function buildBotTickerItems(overview: BotOverview): { items: TickerItem[]; accentColor: string } {
    const rate = overview.suspectedAutomationRate;
    const rateTone = rate > 10 ? 'warning' : rate > 3 ? 'neutral' : 'positive';
    const accentColor = rateTone === 'warning'
        ? COLORS.warning
        : rateTone === 'positive'
            ? COLORS.positive
            : 'var(--neutral-400)';

    const items: TickerItem[] = [
        {
            label: 'Suspected automation',
            value: formatPct(rate, { decimals: 0 }),
            hint: 'of posts',
            tone: rateTone as TickerItem['tone'],
            emphasis: true,
            ariaLabel: `Suspected automation rate ${rate.toFixed(1)} percent`,
        },
        {
            label: 'Coordination',
            value: coordinationLevel(overview.coordinationIndex),
            hint: `${overview.coordinationIndex.toFixed(2)} / 1`,
        },
        {
            label: 'Flagged Posts',
            value: overview.totalFlaggedPosts.toLocaleString(),
        },
        {
            label: 'Detector confidence',
            value: overview.confidence,
            tone: overview.confidence === 'high'
                ? 'positive'
                : overview.confidence === 'low'
                    ? 'warning'
                    : 'neutral',
        },
    ];
    return { items, accentColor };
}

interface NarrativeAmplificationCardProps {
    narrative: NarrativeAmplification;
}

function NarrativeAmplificationCard({ narrative }: NarrativeAmplificationCardProps) {
    const [modalOpen, setModalOpen] = useState(false);
    // Transition-period filter. Backend sanitizes `indicators` at write
    // time (`_sanitize_llm_indicators` in analysis/src/engine/bot.py), but
    // ai_outputs rows written before that fix can still surface in the
    // aggregation window. Drop noise entries so readers never see a leaked
    // `account_age=None days`-style indicator; once the snapshot cache
    // fully rebuilds with only post-fix rows (max 7 days), this filter
    // becomes a no-op and can be removed.
    const whyFlagged = (narrative.whyFlagged ?? []).filter((r) => !isNoiseLabel(r));
    // If the narrative ITSELF is a noise artifact (its label matches the
    // pattern) OR every signal filtered out, suppress the card — showing
    // an amplification card with a title like "ACCOUNT_AGE=NONE DAYS" is
    // worse than showing nothing.
    if (isNoiseLabel(narrative.narrative) || whyFlagged.length === 0) return null;

    const getConfidenceBadge = (confidence: ConfidenceLevel) => {
        switch (confidence) {
            case 'high': return <span className="badge badge-negative" title="High likelihood of automation">High likelihood</span>;
            case 'medium': return <span className="badge badge-warning" title="Medium likelihood of automation">Medium likelihood</span>;
            case 'low': return <span className="badge badge-neutral" title="Low likelihood of automation">Low likelihood</span>;
            default: return null;
        }
    };

    const accentColor = narrative.confidence === 'high' ? COLORS.negative
        : narrative.confidence === 'medium' ? COLORS.warning
        : 'var(--neutral-500)';

    return (
        <>
            <Card>
                <div className="flex items-start justify-between mb-3" style={{ gap: 'var(--space-3)', flexWrap: 'wrap' }}>
                    <div style={{ minWidth: 0 }}>
                        <h4 className="font-semibold">{narrative.narrative}</h4>
                        <div className="flex items-center gap-2 mt-1" style={{ flexWrap: 'wrap' }}>
                            {getConfidenceBadge(narrative.confidence)}
                            <span className="eyebrow num">
                                {narrative.suspectedBotVolume.toLocaleString()} suspected bot posts
                            </span>
                            <a
                                href={deepLinkHref('narratives', { open: String(narrative.id) })}
                                className="example-row-link"
                                title="Open this story on the Political Narratives page"
                            >
                                See the full story ↗
                            </a>
                        </div>
                    </div>
                    <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={() => setModalOpen(true)}
                        aria-haspopup="dialog"
                    >
                        View details
                    </button>
                </div>

                {/* Always visible: Why flagged */}
                <div>
                    <div className="eyebrow mb-2">Why this was flagged</div>
                    <ul style={{ margin: 0, paddingLeft: 'var(--space-5)' }} className="text-sm">
                        {whyFlagged.map((reason, i) => (
                            <li key={i} className="mb-1">{reason}</li>
                        ))}
                    </ul>
                </div>
            </Card>

            <Modal
                isOpen={modalOpen}
                onClose={() => setModalOpen(false)}
                title={narrative.narrative}
                subtitle={`${narrative.suspectedBotVolume.toLocaleString()} suspected bot posts · ${narrative.confidence} likelihood`}
                accentColor={accentColor}
            >
                {/* Example Posts — every row carries an outbound source link
                    when the backend synthesized one (invariant C1). */}
                <div className="mb-4">
                    <div className="eyebrow mb-2">Flagged Posts</div>
                    <PostCardList
                        posts={narrative.examplePosts.map(flaggedExampleToPostCard)}
                        sampleNote="Example flagged posts amplifying this narrative — a sample, not every flag."
                        emptyNote="No individual posts surfaced yet for this narrative."
                    />
                </div>

                {/* Derived chips — rendered only when the backend actually
                    extracted something; an empty section header would read
                    as broken, not as honest. */}
                {narrative.topHashtags.length > 0 && (
                    <div className="mb-4">
                        <div className="eyebrow mb-2">Top Hashtags</div>
                        <div className="flex flex-wrap gap-1">
                            {narrative.topHashtags.map((tag, i) => (
                                <span key={i} className="badge badge-accent">{tag}</span>
                            ))}
                        </div>
                    </div>
                )}

                {narrative.targets.length > 0 && (
                    <div className="mb-4">
                        <div className="eyebrow mb-2">Who this narrative targets</div>
                        <div className="flex flex-wrap gap-1">
                            {narrative.targets.map((target, i) => (
                                <span key={i} className="badge badge-neutral">{target}</span>
                            ))}
                        </div>
                    </div>
                )}

                {/* Why Flagged (repeated in modal for full context) */}
                <div>
                    <div className="eyebrow mb-2">Why this was flagged</div>
                    <ul style={{ margin: 0, paddingLeft: 'var(--space-5)' }} className="text-sm">
                        {whyFlagged.map((reason, i) => (
                            <li key={i} className="mb-1">{reason}</li>
                        ))}
                    </ul>
                </div>
            </Modal>
        </>
    );
}

interface CoordinationSummaryProps {
    data: CoordinationStats;
}

function CoordinationSummary({ data }: CoordinationSummaryProps) {
    return (
        <Card
            title="Coordination Indicators"
            subtitle={<DefinitionChip entry="coordination" label="What counts as coordination?" />}
            headerActions={
                <MethodPopover
                    description="Coordination is detected through behavioral analysis including timing patterns, text similarity, and network analysis."
                    limitations={[
                        'Some legitimate coordinated campaigns may be flagged',
                        'Sophisticated actors may evade detection',
                        'Metrics are indicative, not definitive proof',
                    ]}
                />
            }
        >
            <div className="grid-2 gap-3">
                <div className="flex flex-col gap-2">
                    <div className="flex justify-between items-center" style={{ padding: '6px 0' }}>
                        <span className="text-sm" title="Share of suspected-bot accounts that posted more than once in this sample.">Suspected accounts posting more than once</span>
                        <span className="num font-semibold">{formatPct(data.accountReuse * 100, { decimals: 0 })}</span>
                    </div>
                </div>
                <div className="flex flex-col gap-2">
                    <div className="flex justify-between items-center" style={{ padding: '6px 0', borderBottom: '1px solid var(--neutral-150)' }}>
                        <span className="text-sm">Identical text pairs detected</span>
                        <span className="num font-semibold">{data.identicalTextPairs.toLocaleString()}</span>
                    </div>
                    <div className="flex justify-between items-center" style={{ padding: '6px 0' }}>
                        <span className="text-sm">Avg posts per suspected account</span>
                        <span className="num font-semibold">{data.avgPostsPerSuspectedAccount.toFixed(1)}</span>
                    </div>
                </div>
            </div>
            <div className="card-note mt-4">
                These metrics indicate potential coordination but are not definitive proof of automated or malicious activity.
            </div>
        </Card>
    );
}

interface BehavioralSignalsPanelProps {
    data: BehavioralSignals;
}

/**
 * Renders four behavioural-signal cards as direct children of the outer
 * dashboard grid. Account age + text similarity pair as 6-col siblings on
 * wide screens; the heatmap takes a full row because its 24-col matrix
 * needs horizontal room; link-domain concentration takes the remainder.
 */
function BehavioralSignalsPanel({ data }: BehavioralSignalsPanelProps) {
    return (
        <>
            {/* Row: account age (6) + text similarity (6) — both compact bar lists */}
            <div className="col-span-6">
                <Card title="Account Age Distribution">
                    <div className="flex flex-col gap-2">
                        {data.accountAgeDistribution.map((item, i) => (
                            <div key={i}>
                                <div className="flex justify-between text-sm mb-1">
                                    <span>{item.range}</span>
                                    <span className="num text-muted">{formatPct(item.percentage, { decimals: 0 })}</span>
                                </div>
                                <div
                                    style={{
                                        height: '6px',
                                        background: 'var(--neutral-100)',
                                        borderRadius: '1px',
                                        overflow: 'hidden',
                                    }}
                                >
                                    <div
                                        style={{
                                            width: `${item.percentage}%`,
                                            height: '100%',
                                            background: item.range.includes('<') ? COLORS.warning : COLORS.accent,
                                        }}
                                    />
                                </div>
                            </div>
                        ))}
                    </div>
                    {(() => {
                        // Data-derived caption — no hardcoded conclusion. State
                        // the youngest bucket's actual share, or nothing when it
                        // is absent, rather than asserting over-representation
                        // regardless of what the distribution shows.
                        const youngest = data.accountAgeDistribution.find((i) => i.range.includes('<'));
                        if (!youngest) return null;
                        return (
                            <div className="card-note mt-4">
                                {formatPct(youngest.percentage, { decimals: 0 })} of suspected-bot posts came from
                                accounts in the {youngest.range} bucket.
                            </div>
                        );
                    })()}
                </Card>
            </div>

            <div className="col-span-6">
                <Card title="Text Similarity Distribution">
                    <div className="text-xs text-muted mb-3">
                        Pairwise text similarity across suspected-bot posts. Natural discourse typically sits in the
                        20–30% range. Values above 80% indicate near-duplicate content, a strong indicator of
                        copy-paste amplification.
                    </div>
                    {(() => {
                        // Backend currently emits raw counts for high/medium/low,
                        // which caused values like "3070%" to render when appended
                        // with a % sign. Normalize to percentages locally so the
                        // card is always legible; when backend starts returning
                        // percentages, this still produces the right result
                        // (sum == 100 → no-op).
                        const { high, medium, low } = data.copyPasteSimilarity;
                        const total = high + medium + low;
                        const pct = (n: number) => total > 0 ? Math.round((n / total) * 100) : 0;
                        return (
                            <div className="flex flex-col gap-3">
                                <SimilarityBar
                                    label="High similarity (>80%)"
                                    value={pct(high)}
                                    color="var(--semantic-negative)"
                                />
                                <SimilarityBar
                                    label="Medium similarity (50–80%)"
                                    value={pct(medium)}
                                    color="var(--semantic-warning)"
                                />
                                <SimilarityBar
                                    label="Low similarity (<50%)"
                                    value={pct(low)}
                                    color="var(--accent)"
                                />
                            </div>
                        );
                    })()}
                </Card>
            </div>

            {/* Posting-cadence heatmap dropped: the backend emits day:0 for
                every row and buckets by server-local hour, so the viz can't
                honestly show the per-day UTC cadence its legend claimed.
                Re-introduce once bot.py emits real (day, hour) UTC buckets —
                tracked in docs/todos/ui-rework.md. */}
            <div className="col-span-6">
                <Card title="Link Domain Concentration">
                    <div className="flex flex-col">
                        {data.linkDomainConcentration.map((item, i) => (
                            <div
                                key={i}
                                className="flex justify-between items-center"
                                style={{ padding: '6px 0', borderBottom: '1px solid var(--neutral-150)' }}
                            >
                                <span className="text-sm" style={{ fontFamily: 'var(--font-mono)' }}>
                                    {item.domain}
                                </span>
                                <span className="num text-sm text-muted">{formatPct(item.percentage, { decimals: 0 })}</span>
                            </div>
                        ))}
                    </div>
                    <div className="card-note mt-4">
                        High concentration of links to a small number of domains may indicate coordinated promotion.
                    </div>
                </Card>
            </div>
        </>
    );
}

interface SimilarityBarProps {
    label: string;
    value: number;
    color: string;
}

function SimilarityBar({ label, value, color }: SimilarityBarProps) {
    const title =
        `${label}: ${formatPct(value, { decimals: 0 })} of pairwise text comparisons ` +
        `fall in this similarity band. Natural discourse typically clusters in the ` +
        `low band (<50%); values above 80% indicate near-duplicate posts.`;
    return (
        <div title={title}>
            <div className="flex justify-between text-sm mb-1">
                <span>{label}</span>
                <span className="font-medium" style={{ color }}>{formatPct(value, { decimals: 0 })}</span>
            </div>
            <div
                style={{
                    height: '8px',
                    background: 'var(--neutral-100)',
                    borderRadius: '1px',
                    overflow: 'hidden',
                }}
            >
                <div style={{ width: `${value}%`, height: '100%', background: color }} />
            </div>
        </div>
    );
}

interface BotActivityProfilerProps {
    filters: Filters;
}

function BotActivityProfiler({ filters }: BotActivityProfilerProps) {
    // Windowed like every other tab: the backend pre-computes
    // bot_activity_{window} snapshots and /bot-activity takes ?window=.
    // (This page previously fetched the API's 24h default while labeling
    // it "full sample, not time-windowed" — wrong on both counts.)
    const [activeItem, setActiveItem] = useState<BotEntityItem | null>(null);
    // Cross-page entity deep link ("#bots?entity=subreddit:politics").
    const [entityParam, setEntityParam] = useDeepLinkParam('entity');
    const { data, loading, error, refetch } = useFetch<BotData>(
        () => fetchBotActivity(filters.timeRange),
        [filters.timeRange],
        `bot-activity:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatus>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );

    // Resolve entity= once data lands; unknown entities clear the param.
    useEffect(() => {
        if (!data || !entityParam) return;
        const target = parseEntityParam(entityParam);
        if (target) {
            const lists = [
                data.overview.by_news_outlet,
                data.overview.by_official,
                data.overview.by_general_public,
            ];
            for (const list of lists) {
                const item = (list ?? []).find(
                    (it) => it.kind === target.kind && it.key === target.key,
                );
                if (item) {
                    setActiveItem(item);
                    return;
                }
            }
        }
        setEntityParam(null);
    }, [data, entityParam, setEntityParam]);

    if (error) {
        return <ErrorState message={error.message} onRetry={refetch} />;
    }

    if (loading) {
        return (
            <div className="flex flex-col gap-6">
                <div className="grid-3">
                    <LoadingCard />
                    <LoadingCard />
                    <LoadingCard />
                </div>
                <LoadingCard />
                <LoadingCard />
            </div>
        );
    }

    // Early-return only when the fetch itself yielded nothing. Zero flagged
    // accounts is a legitimate signal (pipeline ran, nothing tripped) — the
    // frame still renders with per-column empty copy, matching Tone's
    // pattern. Previously this hid the whole page, which made a healthy
    // "nothing to flag" run indistinguishable from a broken pipeline.
    if (!data) return <EmptyState title="No bot-activity data available" />;

    const { items: botTickerItems, accentColor: botAccentColor } = buildBotTickerItems(data.overview);
    const botRefreshed = formatRefreshedAgo(
        getSnapshotTimestamp(snapshotStatus, `bot_activity_${filters.timeRange}`),
    );
    // Funnel stage 1: total scanned across the per-entity rollups (the
    // catch-all buckets are included, so this covers every scored doc).
    // Null on pre-rollup snapshots — the funnel renders an em dash.
    const entityLists = [
        data.overview.by_news_outlet,
        data.overview.by_official,
        data.overview.by_general_public,
    ];
    const totalScanned = entityLists.some((l) => (l?.length ?? 0) > 0)
        ? entityLists.flatMap((l) => l ?? []).reduce((s, it) => s + it.total_docs, 0)
        : null;

    return (
        <div className="dashboard-grid">
            <div className="col-span-12">
                <GlobalTicker
                    items={botTickerItems}
                    refreshed={botRefreshed}
                    accentColor={botAccentColor}
                    ariaLabel="Bot detector overview"
                    legend={
                        <MethodPopover
                            title="How to read these numbers"
                            description={
                                'Suspected automation = the share of scored posts our detector flags as likely '
                                + 'automated. Coordination = a 0 (none) to 1 (high) index of timing and content '
                                + 'overlap across suspected accounts. Detector confidence = how much to trust this '
                                + "page's estimates overall, based on sample size and signal agreement. These are "
                                + 'probabilistic leads, not verdicts.'
                            }
                        />
                    }
                />
            </div>

            <div className="col-span-12">
                <div className="reads-as-today">
                    <p className="lead" style={{ margin: 0 }}>{readsAsToday(data)}</p>
                </div>
            </div>

            {/* The chain of evidence — the page's signature strip. */}
            <div className="col-span-12">
                <CoordinationEvidencePanel
                    overview={data.overview}
                    totalScanned={totalScanned}
                />
            </div>

            <div className="col-span-12">
                <BotThreeWayGrid overview={data.overview} onOpen={setActiveItem} />
            </div>

            {activeItem && (
                <BotEntityModal
                    item={activeItem}
                    onClose={() => {
                        setActiveItem(null);
                        if (entityParam) setEntityParam(null);
                    }}
                />
            )}

            {/* Row: coordination summary (5) + narrative amplification section label (7).
                Coord is a compact 4-stat card; the amplification section headline pairs
                with it so the narrative-detail cards below get a clean section start. */}
            <div className="col-span-5">
                <CoordinationSummary data={data.coordinationStats} />
            </div>
            {/* Section-label band. Kept styling light on purpose so it
                reads as a header, not a card — on mobile this would
                otherwise stack as a bordered panel between two real
                Cards and look like an orphan. */}
            <div className="col-span-7 bot-section-label">
                <div className="eyebrow" style={{ marginBottom: 'var(--space-1)' }}>
                    Narratives with Suspected Bot Amplification
                </div>
                <div className="text-xs text-muted" style={{ lineHeight: 'var(--leading-relaxed)' }}>
                    Each row below is a narrative whose amplifying accounts score as likely bots. Open View details to see
                    example posts, flagged hashtags, and the signals that triggered the flag.
                </div>
            </div>

            {/* Narrative amplification cards — each full-width so their
                expanded state has room for the example post grid. */}
            {data.narrativeAmplification.map((narrative) => (
                <div key={narrative.id} className="col-span-12">
                    <NarrativeAmplificationCard narrative={narrative} />
                </div>
            ))}
            {data.narrativeAmplification.length === 0 && (
                <div className="col-span-12">
                    <Card>
                        <p className="text-sm text-muted" style={{ margin: 0 }}>
                            No flagged post in this window belongs to a tracked narrative yet.
                            This section fills in when suspected-bot posts overlap with the
                            recurring claims on the Narratives page.
                        </p>
                    </Card>
                </div>
            )}

            {/* Behavioral signals — four cards rendered as direct grid children.
                See BehavioralSignalsPanel for per-card span decisions. */}
            <BehavioralSignalsPanel data={data.behavioralSignals} />

            <div className="col-span-12">
                <CollapsibleInfo>
                    <p className="text-sm">
                        This page flags accounts and posts in our political-content sample that look
                        automated. The detector scores each account from behavioral signals — posting
                        rate, text repetition, account age, and coordinated timing.
                    </p>
                    <p className="text-sm">
                        Some real users post in bot-like ways, and some real bots post like humans.
                        Treat flags as <strong>leads, not verdicts</strong>: every signal points at
                        the specific behavior that triggered it, so a reader can audit each call.
                    </p>
                </CollapsibleInfo>
            </div>
        </div>
    );
}

export default BotActivityProfiler;
