import { useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, EntityProfileCard, ErrorState, GlobalTicker,
    LoadingCard, MethodPopover, MetricCard, Modal,
    ThreeWayColumn, ThreeWayGrid,
    entityExternalUrl,
} from '../components/common';
import type { TickerItem } from '../components/common';
import { Heatmap } from '../components/charts';
import { fetchBotActivity, fetchSnapshotStatus, type SnapshotStatus } from '../services/api';
import { asOfTodayEyebrow } from '../services/timeWindow';
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

const BOT_TOP_N = 12;

function BotEntityCard({ item }: { item: BotEntityItem }) {
    const profile = item.entity_profile;
    const rateColor = item.bot_rate_pct > 10
        ? COLORS.negative
        : item.bot_rate_pct > 3
            ? COLORS.warning
            : 'var(--neutral-700)';

    return (
        <EntityProfileCard
            profile={profile}
            stats={item.total_docs > 0 ? [
                { label: 'Bot rate',    value: formatPct(item.bot_rate_pct), color: rateColor, emphasis: true },
                { label: 'Bot posts',   value: item.bot_docs.toLocaleString() },
                { label: 'Posts scanned', value: item.total_docs.toLocaleString() },
            ] : []}
            href={entityExternalUrl(profile) ?? undefined}
            emptyNote="Tracked — no posts classified in this window yet."
        />
    );
}

function BotThreeWayGrid({ overview }: { overview: BotOverview }) {
    // Always render the three-way frame when we have an overview, even if
    // individual tiers are empty — per-column empty copy is more honest
    // ("no official X posts scored") than hiding the grid entirely.
    const outlets = (overview.by_news_outlet ?? []).slice(0, BOT_TOP_N);
    const officials = (overview.by_official ?? []).slice(0, BOT_TOP_N);
    const publics = (overview.by_general_public ?? []).slice(0, BOT_TOP_N);
    const renderCard = (it: BotEntityItem) => (
        <BotEntityCard key={`${it.kind}:${it.key}`} item={it} />
    );

    return (
        <ThreeWayGrid>
            <ThreeWayColumn
                header="The News"
                byline="Outlets whose scanned posts classify as bot — should skew near 0%."
                empty="No news posts scored for bot detection."
                isEmpty={outlets.length === 0}
            >
                {outlets.map(renderCard)}
            </ThreeWayColumn>
            <ThreeWayColumn
                header="Politicians & Officials"
                byline="Tracked officeholders on X, ranked by bot-classification rate of their posts."
                empty="No official X posts scored for bot detection."
                isEmpty={officials.length === 0}
            >
                {officials.map(renderCard)}
            </ThreeWayColumn>
            <ThreeWayColumn
                header="The Public"
                byline="Subreddits + the broader X user catch-all — where bot amplification actually lives."
                empty="No public social posts scored for bot detection."
                isEmpty={publics.length === 0}
            >
                {publics.map(renderCard)}
            </ThreeWayColumn>
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
        parts.push(`${count} of them are pushing the same talking point: "${topNarrative.narrative}".`);
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
            label: 'Automation Rate',
            value: formatPct(rate),
            hint: 'of accounts',
            tone: rateTone as TickerItem['tone'],
            emphasis: true,
            ariaLabel: `Suspected automation rate ${rate.toFixed(1)} percent`,
        },
        {
            label: 'Coordination Idx',
            value: overview.coordinationIndex.toFixed(2),
            hint: '0–1',
        },
        {
            label: 'Flagged Accounts',
            value: overview.totalFlaggedAccounts.toLocaleString(),
        },
        {
            label: 'Confidence',
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

interface BotOverviewMetricsProps {
    data: BotOverview;
}

function BotOverviewMetrics({ data }: BotOverviewMetricsProps) {
    // mb-4 removed — the parent `dashboard-grid` already supplies row gap.
    // On mobile (grid-3 collapses to 1-col) the extra 16px margin doubled
    // the spacing between this block and the one below, breaking the
    // otherwise-consistent rhythm.
    return (
        <div className="grid-3">
            <MetricCard
                label="Suspected Automation Rate"
                value={formatPct(data.suspectedAutomationRate, { decimals: 0 })}
                subtitle="of analyzed accounts"
                className={data.suspectedAutomationRate > 10 ? 'border-warning' : ''}
            />
            <MetricCard
                label="Coordination Index"
                value={data.coordinationIndex.toFixed(2)}
                subtitle="0 = none, 1 = highly coordinated"
            />
            <Card title="Top Amplified Clusters">
                <div className="flex flex-wrap gap-1">
                    {data.topClusters.map((cluster, i) => (
                        <span key={i} className="badge badge-warning">{cluster}</span>
                    ))}
                </div>
                <div className="eyebrow mt-3 num">
                    {data.totalFlaggedAccounts.toLocaleString()} accounts flagged
                </div>
            </Card>
        </div>
    );
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
            case 'high': return <span className="badge badge-negative">High likelihood</span>;
            case 'medium': return <span className="badge badge-warning">Medium likelihood</span>;
            case 'low': return <span className="badge badge-neutral">Low likelihood</span>;
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
                        <h4 className="font-semibold uppercase" style={{ letterSpacing: '0.02em' }}>{narrative.narrative}</h4>
                        <div className="flex items-center gap-2 mt-1" style={{ flexWrap: 'wrap' }}>
                            {getConfidenceBadge(narrative.confidence)}
                            <span className="eyebrow num">
                                {narrative.suspectedBotVolume.toLocaleString()} suspected bot posts
                            </span>
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
                    <div className="eyebrow mb-2">Why Flagged · Coordination Indicators</div>
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
                    {narrative.examplePosts.length === 0 ? (
                        <div className="text-sm text-muted" style={{ fontStyle: 'italic' }}>
                            No individual posts surfaced yet for this indicator.
                        </div>
                    ) : (
                        <div className="flex flex-col gap-2">
                            {narrative.examplePosts.map((ex) => (
                                <div
                                    key={ex.doc_id}
                                    style={{
                                        padding: 'var(--space-2) var(--space-3)',
                                        background: 'var(--bg-inset)',
                                        borderLeft: '2px solid var(--neutral-300)',
                                        borderRadius: '2px',
                                    }}
                                >
                                    <div
                                        className="text-sm"
                                        style={{ fontStyle: 'italic', marginBottom: 4 }}
                                    >
                                        "{ex.text}"
                                    </div>
                                    <div
                                        className="text-xs text-muted"
                                        style={{ display: 'flex', gap: 'var(--space-2)', alignItems: 'baseline' }}
                                    >
                                        <span>{ex.source_label}</span>
                                        {ex.url && (
                                            <a
                                                href={ex.url}
                                                target="_blank"
                                                rel="noreferrer"
                                                className="example-row-link"
                                            >
                                                View original ↗
                                            </a>
                                        )}
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>

                {/* Hashtags and Phrases */}
                <div className="grid-2 gap-3 mb-4">
                    <div>
                        <div className="eyebrow mb-2">Top Hashtags</div>
                        <div className="flex flex-wrap gap-1">
                            {narrative.topHashtags.map((tag, i) => (
                                <span key={i} className="badge badge-accent">{tag}</span>
                            ))}
                        </div>
                    </div>
                    <div>
                        <div className="eyebrow mb-2">Key Phrases</div>
                        <div className="flex flex-wrap gap-1">
                            {narrative.topPhrases.map((phrase, i) => (
                                <span key={i} className="badge badge-neutral">{phrase}</span>
                            ))}
                        </div>
                    </div>
                </div>

                {/* Targets */}
                <div className="mb-4">
                    <div className="eyebrow mb-2">Primary Targets</div>
                    <div className="flex flex-wrap gap-1">
                        {narrative.targets.map((target, i) => (
                            <span key={i} className="badge badge-negative">{target}</span>
                        ))}
                    </div>
                </div>

                {/* Why Flagged (repeated in modal for full context) */}
                <div>
                    <div className="eyebrow mb-2">Why Flagged · Coordination Indicators</div>
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
                    <div className="flex justify-between items-center" style={{ padding: '6px 0', borderBottom: '1px solid var(--neutral-150)' }}>
                        <span className="text-sm">Burst timing similarity</span>
                        <span className="num font-semibold">{formatPct(data.burstTimingSimilarity * 100, { decimals: 0 })}</span>
                    </div>
                    <div className="flex justify-between items-center" style={{ padding: '6px 0' }}>
                        <span className="text-sm">Accounts showing reuse patterns</span>
                        <span className="num font-semibold">{data.accountReuse.toLocaleString()}</span>
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
                    <div className="card-note mt-4">
                        Young accounts (&lt;30 days) are over-represented in suspected bot activity.
                    </div>
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

            {/* Row: posting cadence heatmap (8) + link domain concentration (4).
                The 24-col matrix wants width; link domains is a short list that
                fits the narrow slot nicely. */}
            <div className="col-span-8">
                <Card title="Posting Cadence Heatmap">
                    <div className="text-xs text-muted mb-2">
                        Rows are days of the week, columns are hours (0–23, UTC). Darker cells indicate heavier
                        posting volume among suspected-bot accounts. Clusters in off-hours (02:00–05:00 UTC) are a
                        red flag for automation, not proof of it.
                    </div>
                    <Heatmap data={data.postingCadence} cellSize={12} gap={1} />
                    <div className="card-note mt-4">
                        Concentrated activity during off-hours (02:00–05:00 UTC) was detected in this window.
                    </div>
                </Card>
            </div>

            <div className="col-span-4">
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
    // Bot activity is not time-window filtered at the API layer (it's a
    // snapshot cache rebuilt on each pipeline run), so `filters` doesn't
    // influence the key. If that changes, fold the relevant filter fields
    // into the cache key.
    const { data, loading, error, refetch } = useFetch<BotData>(
        () => fetchBotActivity(),
        [],
        'bot-activity',
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatus>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );

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
    const botRefreshed = formatRefreshedAgo(getSnapshotTimestamp(snapshotStatus, 'bot_activity'));

    return (
        <div className="dashboard-grid">
            <div className="col-span-12">
                <GlobalTicker
                    items={botTickerItems}
                    refreshed={botRefreshed}
                    accentColor={botAccentColor}
                    ariaLabel="Bot detector overview"
                />
            </div>

            <div className="col-span-12">
                <div className="reads-as-today">
                    <span className="eyebrow reads-as-today-eyebrow">
                        {asOfTodayEyebrow(filters.timeRange)}
                    </span>
                    <p className="lead" style={{ margin: 0 }}>{readsAsToday(data)}</p>
                </div>
            </div>

            <div className="col-span-12">
                <BotThreeWayGrid overview={data.overview} />
            </div>

            {/* Row: overview metrics — BotOverviewMetrics already renders 3
                items; wrap full-width and let its internal grid-3 handle the
                column layout so we don't double-nest grids. */}
            <div className="col-span-12">
                <BotOverviewMetrics data={data.overview} />
            </div>

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
                    Each row below is a narrative whose amplifying accounts score as likely bots. Expand to see
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
