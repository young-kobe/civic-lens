import { useState } from 'react';
import {
    CollapsibleInfo, EmptyState, EntityHeader, EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, Modal, MoversTicker, SupportingDocsTable,
    ThreeWayColumn, ThreeWayGrid, TierRow, TopMetricsBlock,
    classificationSampleToSupportingDoc, entityExternalUrl, entityLeanAccent, sentimentStats,
} from '../components/common';
import type { TickerItem } from '../components/common';
import { Sparkline } from '../components/charts';
import type {
    EntitySentimentItem, Filters, MoversResult, PollingSocialComparison, PublicSentimentData,
    SentimentDistribution,
} from '../types';
import { fetchMovers, fetchSentiment, fetchSnapshotStatus, type SnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, getSnapshotTimestamp } from '../services/freshness';
import { formatPct } from '../services/format';
import { transformPublicSentiment } from '../services/transformers';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import { TopicDivergencePanel } from './publicSentiment/TopicDivergencePanel';


// --------------------------------------------------------------------------- //
//  Top metrics block — Bloomberg-style dense header                           //
// --------------------------------------------------------------------------- //

interface TierAggregate {
    net: number;
    volume: number;
}

function aggregateTier(items: EntitySentimentItem[] | undefined): TierAggregate {
    if (!items || items.length === 0) return { net: 0, volume: 0 };
    let pos = 0, neg = 0, neu = 0;
    for (const it of items) {
        pos += it.positive;
        neg += it.negative;
        neu += it.neutral;
    }
    const total = pos + neg + neu;
    const net = total > 0 ? ((pos - neg) / total) * 100 : 0;
    return { net: Math.round(net * 10) / 10, volume: total };
}

function toneVerb(net: number): string {
    if (net > 15) return 'clearly positive';
    if (net > 5)  return 'slightly positive';
    if (net < -15) return 'clearly negative';
    if (net < -5)  return 'slightly negative';
    return 'roughly neutral';
}

function toneColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return 'var(--neutral-500)';
}

interface TopMetricsProps {
    data: PublicSentimentData;
    windowLabel: string;
}

function TopMetrics({ data, windowLabel }: TopMetricsProps) {
    const news = aggregateTier(data.byNewsOutlet);
    const officials = aggregateTier(data.byOfficial);
    const pub = aggregateTier(data.byGeneralPublic);

    return (
        <TopMetricsBlock
            eyebrow={`As of ${windowLabel}`}
            meta={`${data.overview.volume.toLocaleString()} posts · ${data.overview.confidence} confidence`}
            aux={
                <>
                    {data.gopFavorability && (
                        <GOPMini
                            favorability={data.gopFavorability}
                            trend={data.gopTrend ?? undefined}
                        />
                    )}
                    <IntensityMini distribution={data.distribution} />
                </>
            }
        >
            <ToneTierRow label="News articles are" agg={news} />
            <ToneTierRow label="Officials are" agg={officials} />
            <ToneTierRow label="The public is" agg={pub} />
        </TopMetricsBlock>
    );
}

function ToneTierRow({ label, agg }: { label: string; agg: TierAggregate }) {
    const color = toneColor(agg.net);
    const axisPct = ((agg.net + 100) / 200) * 100;
    const hasData = agg.volume > 0;

    return (
        <TierRow
            label={label}
            value={hasData ? formatPct(agg.net, { min: -100, signed: true }) : '—'}
            valueColor={color}
            verb={hasData
                ? `${toneVerb(agg.net)} · ${agg.volume.toLocaleString()} posts`
                : 'no posts yet'}
            showZeroTick
            dotPct={hasData ? axisPct : undefined}
            dotColor={hasData ? color : undefined}
        />
    );
}

function GOPMini({
    favorability,
    trend,
}: {
    favorability: NonNullable<PublicSentimentData['gopFavorability']>;
    trend?: Array<{ date: string; value: number }>;
}) {
    const color = favorability.netFavorability > 0
        ? COLORS.positive
        : favorability.netFavorability < 0
            ? COLORS.negative
            : 'var(--neutral-500)';
    const total = favorability.favorable + favorability.unfavorable + favorability.neutral;
    const favPct = total > 0 ? (favorability.favorable / total) * 100 : 0;
    const unfavPct = total > 0 ? (favorability.unfavorable / total) * 100 : 0;
    const neuPct = total > 0 ? (favorability.neutral / total) * 100 : 0;

    const hasTrend = Boolean(trend && trend.length > 1);
    const sampleSize = total.toLocaleString();

    // Native `title` tooltips on the widgets keep the page uncluttered —
    // no popover UI, no extra JS, long-press works on touch. Each tooltip
    // names what the widget shows, the numeric breakdown, and the sample
    // size so a reader can tell at a glance whether a number is
    // meaningful or from a sparse bucket.
    const trendTitle = hasTrend
        ? `Daily net GOP favorability over the last ${trend!.length} days in this filter. ` +
          `Sample: ${sampleSize} posts.`
        : 'Not enough daily points in this filter to draw a trend.';
    const barTitle =
        `GOP stance distribution across ${sampleSize} sampled posts: ` +
        `${favPct.toFixed(0)}% favorable · ${neuPct.toFixed(0)}% neutral · ${unfavPct.toFixed(0)}% unfavorable.`;

    return (
        <div className="mini-metric" title={`Net GOP favorability: ${formatPct(favorability.netFavorability, { min: -100, signed: true })} across ${sampleSize} sampled posts.`}>
            <span className="mini-metric-label">GOP party stance</span>
            <span className="mini-metric-value" style={{ color }}>
                {formatPct(favorability.netFavorability, { min: -100, signed: true })}
            </span>
            <span className="mini-metric-visual">
                {hasTrend ? (
                    <span className="mini-metric-trend" aria-hidden title={trendTitle}>
                        <Sparkline
                            data={trend!}
                            dataKey="value"
                            xKey="date"
                            height={22}
                            color={color}
                            showTooltip={false}
                        />
                    </span>
                ) : (
                    <span
                        className="mini-metric-trend mini-metric-trend-empty"
                        aria-hidden
                        title={trendTitle}
                    />
                )}
                <span
                    className="mini-metric-bar"
                    aria-label={barTitle}
                    title={barTitle}
                >
                    <span className="mini-bar-favorable" style={{ width: `${favPct}%` }} />
                    <span className="mini-bar-neutral"  style={{ width: `${neuPct}%` }} />
                    <span className="mini-bar-unfavorable" style={{ width: `${unfavPct}%` }} />
                </span>
            </span>
        </div>
    );
}

function IntensityMini({ distribution }: { distribution: SentimentDistribution }) {
    const total = distribution.strongPositive + distribution.mildPositive
        + distribution.neutral + distribution.mildNegative + distribution.strongNegative;

    // Never return null — the parent `.top-metrics-aux` is a 2-column
    // grid. If IntensityMini disappears, the grid loses one cell and
    // GOPMini expands to fill both columns, which looks like a different
    // page depending on whether the filter yielded distribution data.
    // Instead render an "—" placeholder that occupies the same slot.
    if (total === 0) {
        return (
            <div className="mini-metric">
                <span className="mini-metric-label">Tone intensity</span>
                <span className="mini-metric-value mini-metric-value-muted">—</span>
                <span className="mini-metric-visual">
                    <span
                        className="mini-metric-bar mini-intensity mini-metric-bar-empty"
                        aria-label="No tone distribution in this filter"
                        title="No distribution data for the current filter."
                    />
                </span>
            </div>
        );
    }

    const pct = (n: number) => (n / total) * 100;
    // Label the biggest bucket.
    const buckets: Array<[string, number, string]> = [
        ['strongly positive', distribution.strongPositive, 'Strong +'],
        ['mild positive',     distribution.mildPositive,   'Mild +'],
        ['neutral',           distribution.neutral,        'Neutral'],
        ['mild negative',     distribution.mildNegative,   'Mild −'],
        ['strongly negative', distribution.strongNegative, 'Strong −'],
    ];
    const biggest = buckets.reduce((a, b) => (a[1] >= b[1] ? a : b));
    const biggestPct = (biggest[1] / total) * 100;

    const sampleSize = total.toLocaleString();
    const barTitle =
        `Tone intensity across ${sampleSize} sampled posts: ` +
        `${pct(distribution.strongPositive).toFixed(0)}% strong + · ` +
        `${pct(distribution.mildPositive).toFixed(0)}% mild + · ` +
        `${pct(distribution.neutral).toFixed(0)}% neutral · ` +
        `${pct(distribution.mildNegative).toFixed(0)}% mild − · ` +
        `${pct(distribution.strongNegative).toFixed(0)}% strong −.`;
    const hintTitle =
        `${formatPct(biggestPct, { decimals: 0 })} of ${sampleSize} posts fall in the "${biggest[0]}" bucket.`;

    // Same .mini-metric-visual wrapper pattern as GOPMini — the visuals
    // (bar + hint) live inside a flex container so the top-level grid
    // sees a consistent 3-column shape regardless of child count.
    return (
        <div
            className="mini-metric"
            title={`Tone intensity distribution across ${sampleSize} sampled posts.`}
        >
            <span className="mini-metric-label">Tone intensity</span>
            <span className="mini-metric-value">
                most {biggest[0]}
            </span>
            <span className="mini-metric-visual">
                <span
                    className="mini-metric-bar mini-intensity"
                    aria-label={barTitle}
                    title={barTitle}
                >
                    <span className="mini-bar-strongpos" style={{ width: `${pct(distribution.strongPositive)}%` }} />
                    <span className="mini-bar-mildpos"   style={{ width: `${pct(distribution.mildPositive)}%` }} />
                    <span className="mini-bar-neu"       style={{ width: `${pct(distribution.neutral)}%` }} />
                    <span className="mini-bar-mildneg"   style={{ width: `${pct(distribution.mildNegative)}%` }} />
                    <span className="mini-bar-strongneg" style={{ width: `${pct(distribution.strongNegative)}%` }} />
                </span>
                <span className="mini-metric-hint" title={hintTitle}>
                    {formatPct(biggestPct, { decimals: 0 })} of posts
                </span>
            </span>
        </div>
    );
}


// --------------------------------------------------------------------------- //
//  Three-way grid                                                             //
// --------------------------------------------------------------------------- //

const TOP_N = 12;

interface ThreeWayGridProps {
    newsOutlets: EntitySentimentItem[];
    officials: EntitySentimentItem[];
    generalPublic: EntitySentimentItem[];
    confidence: PublicSentimentData['overview']['confidence'];
    onOpen: (item: EntitySentimentItem) => void;
}

function SentimentThreeWayGrid({
    newsOutlets, officials, generalPublic, confidence, onOpen,
}: ThreeWayGridProps) {
    const news = newsOutlets.slice(0, TOP_N);
    const offs = officials.slice(0, TOP_N);
    const pub = generalPublic.slice(0, TOP_N);
    const renderCard = (item: EntitySentimentItem) => (
        <EntityProfileCard
            key={item.key}
            profile={item.entityProfile}
            stats={item.volume > 0
                ? sentimentStats({ netTone: item.netScore, volume: item.volume, confidence })
                : []}
            onClick={() => onOpen(item)}
        />
    );
    return (
        <ThreeWayGrid>
            <ThreeWayColumn
                header="The News"
                byline="Top outlets by coverage volume, with their editorial lean"
                empty="No news articles in this window."
                isEmpty={news.length === 0}
            >
                {news.map(renderCard)}
            </ThreeWayColumn>
            <ThreeWayColumn
                header="Politicians & Officials"
                byline="Tracked officeholders posting on X"
                empty="No officials have posted in this window yet."
                isEmpty={offs.length === 0}
            >
                {offs.map(renderCard)}
            </ThreeWayColumn>
            <ThreeWayColumn
                header="The Public"
                byline="Subreddits + the broader X user catch-all"
                empty="No social posts in this window."
                isEmpty={pub.length === 0}
            >
                {pub.map(renderCard)}
            </ThreeWayColumn>
        </ThreeWayGrid>
    );
}


// --------------------------------------------------------------------------- //
//  Entity detail modal (sentiment page)                                        //
// --------------------------------------------------------------------------- //

function EntitySentimentModal({
    item, onClose,
}: {
    item: EntitySentimentItem;
    onClose: () => void;
}) {
    const { entityProfile: profile, netScore, volume, classificationSamples } = item;
    const sourceUrl = entityExternalUrl(profile);

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle={buildEntitySubtitle(profile)}
            accentColor={entityLeanAccent(profile)}
        >
            <EntityHeader profile={profile} />

            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow">How they lean</div>
                    <div className="metric-value">
                        {formatPct(netScore, { min: -100, signed: true })}
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Posts scored</div>
                    <div className="metric-value">{volume.toLocaleString()}</div>
                </div>
            </div>

            {(sourceUrl || profile.leanSource || profile.bioSource) && (
                <div className="entity-modal-links">
                    {sourceUrl && (
                        <a href={sourceUrl} target="_blank" rel="noreferrer">
                            Visit {profile.displayName} ↗
                        </a>
                    )}
                    {profile.leanSource && (
                        <span className="text-xs text-muted">
                            Lean source: {profile.leanSource}
                        </span>
                    )}
                    {profile.kind === 'official' && profile.bioSource && (
                        <a href={profile.bioSource} target="_blank" rel="noreferrer">Bio ↗</a>
                    )}
                </div>
            )}

            {classificationSamples && classificationSamples.length > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">Recent classified posts</h3>
                    <SupportingDocsTable
                        docs={classificationSamples.map(classificationSampleToSupportingDoc)}
                    />
                </>
            )}
        </Modal>
    );
}

function buildEntitySubtitle(profile: EntitySentimentItem['entityProfile']): string | undefined {
    if (profile.kind === 'outlet') {
        const parts = [profile.owner, profile.founded ? `est. ${profile.founded}` : null, profile.lean]
            .filter(Boolean);
        return parts.length > 0 ? parts.join(' · ') : undefined;
    }
    if (profile.kind === 'official') {
        return [profile.office, profile.party].filter(Boolean).join(' · ');
    }
    if (profile.kind === 'subreddit') {
        return [profile.subscriberCountProxy, profile.lean].filter(Boolean).join(' · ');
    }
    return undefined;
}


// --------------------------------------------------------------------------- //
//  Polling-vs-online comparison — kept as a small collapsible below            //
// --------------------------------------------------------------------------- //

function PollingComparison({ data }: { data: PollingSocialComparison }) {
    return (
        <CollapsibleInfo summary="Online stance vs. live polling">
            <p className="text-xs text-muted">
                Our GOP-stance number is derived from sampled online discussion — it's not a
                scientific poll. For reference, here's {data.pollingData?.source || 'the latest polling'}:
            </p>
            <div className="polling-pair">
                <div>
                    <div className="eyebrow">Online stance (ours)</div>
                    <div className="text-sm">
                        favorable {formatPct(data.onlineSentiment?.favorable ?? 0, { decimals: 0 })} · unfavorable{' '}
                        {formatPct(data.onlineSentiment?.unfavorable ?? 0, { decimals: 0 })}
                    </div>
                </div>
                {data.pollingData && (
                    <div>
                        <div className="eyebrow">Live polling</div>
                        <div className="text-sm">
                            favorable {formatPct(data.pollingData.favorable, { decimals: 0 })} · unfavorable {formatPct(data.pollingData.unfavorable, { decimals: 0 })}
                            {data.pollingData.date && (
                                <span className="text-muted"> · {data.pollingData.date}</span>
                            )}
                        </div>
                    </div>
                )}
            </div>
        </CollapsibleInfo>
    );
}


// --------------------------------------------------------------------------- //
//  How-this-works collapsible                                                  //
// --------------------------------------------------------------------------- //

function HowThisWorks() {
    return (
        <CollapsibleInfo>
            <p className="text-sm">
                We aggregate news articles and X posts about US politics from the
                last 30 days, then score each one for tone (positive / negative / neutral) with
                evidence-span validation. Tracked outlets and officials each get their own
                profile card; everything else rolls up into the general-public column or into an
                "Other" catch-all bucket.
            </p>
            <p className="text-sm">
                Tone is a classification of what the post says, not what the author feels. Don't
                read it as opinion polling. Sarcasm and irony are flagged when the model detects
                them but can still be misclassified.
            </p>
        </CollapsibleInfo>
    );
}


// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

function buildSentimentTickerItems(data: PublicSentimentData): TickerItem[] {
    const overall = data.overview;
    const tone: TickerItem['tone'] = overall.netScore > 10 ? 'accent'
        : overall.netScore < -10 ? 'negative' : 'neutral';
    const items: TickerItem[] = [
        {
            label: 'Overall tone',
            value: formatPct(overall.netScore, { min: -100, signed: true }),
            tone,
            emphasis: true,
            ariaLabel: `Overall tone ${formatPct(overall.netScore, { min: -100, signed: true })}`,
        },
        { label: 'Posts scored', value: overall.volume.toLocaleString() },
        { label: 'Confidence', value: overall.confidence },
    ];
    if (data.gopFavorability) {
        items.push({
            label: 'GOP stance',
            value: formatPct(data.gopFavorability.netFavorability, { min: -100, signed: true }),
        });
    }
    return items;
}

/** Headline naming the tier with the most tonally-different read on the
 *  day, plus the single biggest topic divergence when present. */
/**
 * Static framing sentence for the Overall Tone page. Earlier versions
 * templated in per-tier net scores ("News outlets are reading most positive
 * (+4.2%) while the public..."), which leaked internal metric language
 * ("tiers", "dominant divergence") into a banner that non-technical readers
 * skim first. The static sentence frames the page without pretending to
 * summarize the data — the grid and divergence panel below do the actual
 * summarizing in shapes they're built for.
 */
function readsAsToday(_data: PublicSentimentData): string {
    return 'How news outlets, public officials, and everyday people are reading American politics.';
}


interface PublicSentimentProps {
    filters: Filters;
}

function PublicSentiment({ filters }: PublicSentimentProps) {
    const [activeEntity, setActiveEntity] = useState<EntitySentimentItem | null>(null);
    const { data, loading, error, refetch } = useFetch<PublicSentimentData>(
        async () => transformPublicSentiment(await fetchSentiment(filters.timeRange, filters.sourceType)),
        [filters.timeRange, filters.sourceType],
        `sentiment:${filters.timeRange}:${filters.sourceType}`,
    );
    const { data: movers } = useFetch<MoversResult>(
        () => fetchMovers(filters.timeRange),
        [filters.timeRange],
        `movers:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatus>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;

    if (loading) {
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

    if (!data) return <EmptyState title="No tone data available" />;

    const tickerItems = buildSentimentTickerItems(data);
    const refreshed = formatRefreshedAgo(
        getSnapshotTimestamp(snapshotStatus, `sentiment_${filters.timeRange}`),
    );

    return (
        <div className="dashboard-grid">
            <div className="col-span-12">
                <GlobalTicker
                    items={tickerItems}
                    refreshed={refreshed}
                    ariaLabel="Overall tone overview"
                />
            </div>

            {movers && (
                <div className="col-span-12">
                    <MoversTicker data={movers} />
                </div>
            )}

            <div className="col-span-12">
                <div className="reads-as-today">
                    <span className="eyebrow reads-as-today-eyebrow">
                        {asOfTodayEyebrow(filters.timeRange)}
                    </span>
                    <p className="lead" style={{ margin: 0 }}>{readsAsToday(data)}</p>
                </div>
            </div>

            {/* Compact top-metrics block — tier tones + GOP + intensity. */}
            <div className="col-span-12">
                <TopMetrics data={data} windowLabel={formatTimeWindow(filters.timeRange)} />
            </div>

            {/* Three-way grid: News / Officials / Public. */}
            <div className="col-span-12">
                <SentimentThreeWayGrid
                    newsOutlets={data.byNewsOutlet ?? []}
                    officials={data.byOfficial ?? []}
                    generalPublic={data.byGeneralPublic ?? []}
                    confidence={data.overview.confidence}
                    onOpen={setActiveEntity}
                />
            </div>

            {activeEntity && (
                <EntitySentimentModal
                    item={activeEntity}
                    onClose={() => setActiveEntity(null)}
                />
            )}

            {/* Topic divergence panel. */}
            <div className="col-span-12">
                <TopicDivergencePanel topics={data.byTopic} />
            </div>

            {/* Polling-vs-online collapsible (optional). */}
            {data.pollingVsSocial && (
                <div className="col-span-12">
                    <PollingComparison data={data.pollingVsSocial} />
                </div>
            )}

            {/* How this page works — self-documenting content + collapsible backup. */}
            <div className="col-span-12">
                <HowThisWorks />
            </div>
        </div>
    );
}

export default PublicSentiment;
