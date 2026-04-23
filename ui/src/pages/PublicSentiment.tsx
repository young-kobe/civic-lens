import { useState } from 'react';
import {
    CollapsibleInfo, EmptyState, EntityHeader, EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, Modal, MoversTicker, SupportingDocsTable,
    TierRow, TopMetricsBlock,
    classificationSampleToSupportingDoc, entityExternalUrl, entityLeanAccent, sentimentStats,
} from '../components/common';
import type { TickerItem } from '../components/common';
import { Sparkline } from '../components/charts';
import type {
    EntitySentimentItem, Filters, MoversResult, PollingSocialComparison, PublicSentimentData,
    SentimentDistribution,
} from '../types';
import { fetchMovers, fetchSentiment } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
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
    const sign = agg.net >= 0 ? '+' : '';
    const color = toneColor(agg.net);
    const axisPct = ((agg.net + 100) / 200) * 100;
    const hasData = agg.volume > 0;

    return (
        <TierRow
            label={label}
            value={hasData ? `${sign}${agg.net.toFixed(1)}%` : '—'}
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
    const sign = favorability.netFavorability >= 0 ? '+' : '';
    const color = favorability.netFavorability > 0
        ? COLORS.positive
        : favorability.netFavorability < 0
            ? COLORS.negative
            : 'var(--neutral-500)';
    const total = favorability.favorable + favorability.unfavorable + favorability.neutral;
    const favPct = total > 0 ? (favorability.favorable / total) * 100 : 0;
    const unfavPct = total > 0 ? (favorability.unfavorable / total) * 100 : 0;
    const neuPct = total > 0 ? (favorability.neutral / total) * 100 : 0;

    return (
        <div className="mini-metric">
            <span className="mini-metric-label">GOP party stance</span>
            <span className="mini-metric-value" style={{ color }}>
                {sign}{favorability.netFavorability.toFixed(1)}%
            </span>
            {trend && trend.length > 1 && (
                <span className="mini-metric-trend" aria-hidden>
                    <Sparkline
                        data={trend}
                        dataKey="value"
                        xKey="date"
                        height={22}
                        color={color}
                        showTooltip={false}
                    />
                </span>
            )}
            <span className="mini-metric-bar" aria-label="Stance distribution">
                <span className="mini-bar-favorable" style={{ width: `${favPct}%` }} />
                <span className="mini-bar-neutral"  style={{ width: `${neuPct}%` }} />
                <span className="mini-bar-unfavorable" style={{ width: `${unfavPct}%` }} />
            </span>
        </div>
    );
}

function IntensityMini({ distribution }: { distribution: SentimentDistribution }) {
    const total = distribution.strongPositive + distribution.mildPositive
        + distribution.neutral + distribution.mildNegative + distribution.strongNegative;
    if (total === 0) return null;
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

    return (
        <div className="mini-metric">
            <span className="mini-metric-label">Tone intensity</span>
            <span className="mini-metric-value">
                most {biggest[0]}
            </span>
            <span className="mini-metric-bar mini-intensity" aria-label="Tone intensity distribution">
                <span className="mini-bar-strongpos" style={{ width: `${pct(distribution.strongPositive)}%` }} />
                <span className="mini-bar-mildpos"   style={{ width: `${pct(distribution.mildPositive)}%` }} />
                <span className="mini-bar-neu"       style={{ width: `${pct(distribution.neutral)}%` }} />
                <span className="mini-bar-mildneg"   style={{ width: `${pct(distribution.mildNegative)}%` }} />
                <span className="mini-bar-strongneg" style={{ width: `${pct(distribution.strongNegative)}%` }} />
            </span>
            <span className="mini-metric-hint">
                {biggestPct.toFixed(0)}% of posts
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

function ThreeWayGrid({
    newsOutlets, officials, generalPublic, confidence, onOpen,
}: ThreeWayGridProps) {
    return (
        <div className="three-way-grid">
            <ThreeWayColumn
                header="The News"
                byline="Top outlets by coverage volume, with their editorial lean"
                items={newsOutlets.slice(0, TOP_N)}
                confidence={confidence}
                emptyCopy="No news articles in this window."
                onOpen={onOpen}
            />
            <ThreeWayColumn
                header="Politicians & Officials"
                byline="Tracked officeholders posting on X"
                items={officials.slice(0, TOP_N)}
                confidence={confidence}
                emptyCopy="No officials have posted in this window yet."
                onOpen={onOpen}
            />
            <ThreeWayColumn
                header="The Public"
                byline="Subreddits + the broader X user catch-all"
                items={generalPublic.slice(0, TOP_N)}
                confidence={confidence}
                emptyCopy="No social posts in this window."
                onOpen={onOpen}
            />
        </div>
    );
}

interface ThreeWayColumnProps {
    header: string;
    byline: string;
    items: EntitySentimentItem[];
    confidence: PublicSentimentData['overview']['confidence'];
    emptyCopy: string;
    onOpen: (item: EntitySentimentItem) => void;
}

function ThreeWayColumn({ header, byline, items, confidence, emptyCopy, onOpen }: ThreeWayColumnProps) {
    return (
        <div className="three-way-column">
            <div>
                <div className="three-way-column-header">{header}</div>
                <div className="three-way-column-byline">{byline}</div>
            </div>
            {items.length === 0 ? (
                <p className="text-xs text-muted" style={{ padding: 'var(--space-3)' }}>
                    {emptyCopy}
                </p>
            ) : (
                items.map((item) => (
                    <EntityProfileCard
                        key={item.key}
                        profile={item.entityProfile}
                        stats={item.volume > 0
                            ? sentimentStats({ netTone: item.netScore, volume: item.volume, confidence })
                            : []}
                        onClick={() => onOpen(item)}
                    />
                ))
            )}
        </div>
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
    const sign = netScore >= 0 ? '+' : '';
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
                        {sign}{netScore.toFixed(1)}%
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
                        favorable {data.onlineSentiment?.favorable ?? 0}% · unfavorable{' '}
                        {data.onlineSentiment?.unfavorable ?? 0}%
                    </div>
                </div>
                {data.pollingData && (
                    <div>
                        <div className="eyebrow">Live polling</div>
                        <div className="text-sm">
                            favorable {data.pollingData.favorable}% · unfavorable {data.pollingData.unfavorable}%
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
    const sign = overall.netScore >= 0 ? '+' : '';
    const tone: TickerItem['tone'] = overall.netScore > 10 ? 'accent'
        : overall.netScore < -10 ? 'negative' : 'neutral';
    const items: TickerItem[] = [
        {
            label: 'Overall tone',
            value: `${sign}${overall.netScore.toFixed(1)}%`,
            tone,
            emphasis: true,
            ariaLabel: `Overall tone ${sign}${overall.netScore.toFixed(1)} percent`,
        },
        { label: 'Posts scored', value: overall.volume.toLocaleString() },
        { label: 'Confidence', value: overall.confidence },
    ];
    if (data.gopFavorability) {
        const gopSign = data.gopFavorability.netFavorability >= 0 ? '+' : '';
        items.push({
            label: 'GOP stance',
            value: `${gopSign}${data.gopFavorability.netFavorability.toFixed(1)}%`,
        });
    }
    return items;
}

/** Headline naming the tier with the most tonally-different read on the
 *  day, plus the single biggest topic divergence when present. */
function readsAsToday(data: PublicSentimentData): string {
    const tiers: Array<[string, number, number]> = [
        ['News outlets', aggregateTier(data.byNewsOutlet).net, aggregateTier(data.byNewsOutlet).volume],
        ['Officials',    aggregateTier(data.byOfficial).net,    aggregateTier(data.byOfficial).volume],
        ['The public',   aggregateTier(data.byGeneralPublic).net, aggregateTier(data.byGeneralPublic).volume],
    ];
    const withVolume = tiers.filter(([, , v]) => v > 0);
    if (withVolume.length === 0) return 'No scored posts in this window yet.';

    withVolume.sort((a, b) => a[1] - b[1]);
    const lowest = withVolume[0];
    const highest = withVolume[withVolume.length - 1];
    const parts: string[] = [];
    if (withVolume.length >= 2 && highest[1] - lowest[1] >= 10) {
        parts.push(`${highest[0]} are reading most positive (${highest[1] >= 0 ? '+' : ''}${highest[1].toFixed(1)}%) while ${lowest[0].toLowerCase()} read most negative (${lowest[1] >= 0 ? '+' : ''}${lowest[1].toFixed(1)}%).`);
    } else {
        parts.push(`All three tiers reading within a few points of each other — no dominant divergence today.`);
    }
    return parts.join(' ');
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
    const refreshed = new Date().toISOString().slice(0, 19).replace('T', ' ') + ' UTC';

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
                <ThreeWayGrid
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
