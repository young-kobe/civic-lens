import { useEffect, useMemo, useState } from 'react';
import {
    CollapsibleInfo, EmptyState, EntityHeader, EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, MethodPopover, Modal, SupportingDocsTable,
    ThreeWayColumn, ThreeWayGrid, TierRow, TopMetricsBlock,
    classificationSampleToSupportingDoc, entityExternalUrl, entityLeanAccent, sentimentStats,
} from '../components/common';
import type { TickerItem } from '../components/common';
import type {
    ClassificationSample, EntitySentimentItem, Filters,
    PollingSocialComparison, PublicSentimentData, SentimentBreakdown, SentimentDistribution,
} from '../types';
import { fetchSentiment, fetchSnapshotStatus, type SnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, getSnapshotTimestamp } from '../services/freshness';
import { formatPct, formatPts } from '../services/format';
import { transformPublicSentiment } from '../services/transformers';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import {
    TOPICS, matchesTopic, topicByKey, topicFromSlug,
    type Topic, type TopicKey,
} from '../services/topics';
import { TopicDivergencePanel } from './publicSentiment/TopicDivergencePanel';
import { TopicTabBar } from './publicSentiment/TopicTabBar';


// --------------------------------------------------------------------------- //
//  Top metrics block — Bloomberg-style dense header                           //
// --------------------------------------------------------------------------- //

interface TierAggregate {
    net: number | null;     // null = no data for this tier under the active filter
    volume: number;
}

function aggregateTier(items: EntitySentimentItem[] | undefined): TierAggregate {
    if (!items || items.length === 0) return { net: null, volume: 0 };
    let pos = 0, neg = 0, neu = 0;
    for (const it of items) {
        pos += it.positive;
        neg += it.negative;
        neu += it.neutral;
    }
    const total = pos + neg + neu;
    if (total === 0) return { net: null, volume: 0 };
    const net = ((pos - neg) / total) * 100;
    return { net: Math.round(net * 10) / 10, volume: total };
}

/** Tier aggregates pulled from a single per-topic byTopic row. Returns
 *  null-net for tiers the aggregator marked as having zero volume on the
 *  topic — the UI distinguishes that from a real zero-net reading. */
function aggregateTopicTier(
    topicRow: SentimentBreakdown | null,
    tier: 'news' | 'officials' | 'public',
): TierAggregate {
    if (!topicRow) return { net: null, volume: 0 };
    const netKey = tier === 'news' ? 'newsNet' : tier === 'officials' ? 'officialsNet' : 'publicNet';
    const volKey = tier === 'news' ? 'newsVolume' : tier === 'officials' ? 'officialsVolume' : 'publicVolume';
    const net = topicRow[netKey] as number | null | undefined;
    const volume = (topicRow[volKey] as number | undefined) ?? 0;
    if (net == null || volume === 0) return { net: null, volume };
    return { net: Math.round(net * 10) / 10, volume };
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
    activeTopic: Topic;
    /** Per-topic row matching `activeTopic`, or null when activeTopic is
     *  'all'. Pre-resolved by the parent so TopMetrics doesn't repeat the
     *  lookup. */
    topicRow: SentimentBreakdown | null;
}

function TopMetrics({ data, windowLabel, activeTopic, topicRow }: TopMetricsProps) {
    const isFiltered = activeTopic.key !== 'all';

    // When filtered: tier rows derive from the per-topic three-way row in
    // data.byTopic (already pre-computed by the aggregator). When not:
    // fall back to the global per-tier entity rollups.
    const news      = isFiltered
        ? aggregateTopicTier(topicRow, 'news')
        : aggregateTier(data.byNewsOutlet);
    const officials = isFiltered
        ? aggregateTopicTier(topicRow, 'officials')
        : aggregateTier(data.byOfficial);
    const pub       = isFiltered
        ? aggregateTopicTier(topicRow, 'public')
        : aggregateTier(data.byGeneralPublic);

    const eyebrow = isFiltered
        ? `${activeTopic.label} · As of ${windowLabel}`
        : `As of ${windowLabel}`;

    const filteredVolume = (news.volume) + (officials.volume) + (pub.volume);
    const meta = isFiltered
        ? `${filteredVolume.toLocaleString()} posts on ${activeTopic.label}`
        : `${data.overview.volume.toLocaleString()} posts`;

    return (
        <TopMetricsBlock
            eyebrow={eyebrow}
            meta={meta}
            aux={<IntensityMini distribution={data.distribution} allTopics={isFiltered} />}
        >
            <ToneTierRow label="News articles are" agg={news} />
            <ToneTierRow label="Officials are" agg={officials} />
            <ToneTierRow label="The public is" agg={pub} />
        </TopMetricsBlock>
    );
}

function ToneTierRow({ label, agg }: { label: string; agg: TierAggregate }) {
    const hasData = agg.net !== null;
    const color = hasData ? toneColor(agg.net!) : 'var(--neutral-500)';
    const axisPct = hasData ? ((agg.net! + 100) / 200) * 100 : undefined;

    return (
        <TierRow
            label={label}
            value={hasData ? formatPts(agg.net!) : '—'}
            valueColor={color}
            verb={hasData
                ? `${toneVerb(agg.net!)} · ${agg.volume.toLocaleString()} sampled posts`
                : 'no posts on this topic'}
            showZeroTick
            dotPct={axisPct}
            dotColor={hasData ? color : undefined}
        />
    );
}

function IntensityMini({ distribution, allTopics = false }: { distribution: SentimentDistribution; allTopics?: boolean }) {
    // The distribution is a site-wide rollup, not topic-scoped. Inside a
    // topic filter, mark the label "all topics" so it isn't misread as the
    // filtered topic's intensity (R-2).
    const label = allTopics ? 'Tone intensity · all topics' : 'Tone intensity';
    const total = distribution.strongPositive + distribution.mildPositive
        + distribution.neutral + distribution.mildNegative + distribution.strongNegative;

    if (total === 0) {
        return (
            <div className="mini-metric">
                <span className="mini-metric-label">{label}</span>
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

    return (
        <div
            className="mini-metric"
            title={`Tone intensity distribution across ${sampleSize} sampled posts.`}
        >
            <span className="mini-metric-label">{label}</span>
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
    onOpen: (item: EntitySentimentItem) => void;
    activeTopic: Topic;
}

function SentimentThreeWayGrid({
    newsOutlets, officials, generalPublic, onOpen, activeTopic,
}: ThreeWayGridProps) {
    const news = newsOutlets.slice(0, TOP_N);
    const offs = officials.slice(0, TOP_N);
    const pub = generalPublic.slice(0, TOP_N);
    const renderCard = (item: EntitySentimentItem) => (
        <EntityProfileCard
            key={item.key}
            profile={item.entityProfile}
            stats={item.volume > 0
                ? sentimentStats({ netTone: item.netScore, volume: item.volume })
                : []}
            onClick={() => onOpen(item)}
        />
    );

    // When a topic is active we keep the same global entity cards (the
    // backend doesn't yet expose per-entity per-topic rollups — see PR
    // description). The bylines reflect that, and clicking a card opens
    // a modal that DOES filter its evidence to the active topic, so the
    // user-visible drill-down is honest even when the card-level score
    // remains global.
    const topicSuffix = activeTopic.key === 'all'
        ? ''
        : ` · scores cover all topics; click a card to see its ${activeTopic.label} posts`;

    return (
        <ThreeWayGrid>
            <ThreeWayColumn
                header="The News"
                byline={`Top outlets by coverage volume, with their editorial lean${topicSuffix}`}
                empty="No news articles in this window."
                isEmpty={news.length === 0}
            >
                {news.map(renderCard)}
            </ThreeWayColumn>
            <ThreeWayColumn
                header="Politicians & Officials"
                byline={`Tracked officeholders posting on X${topicSuffix}`}
                empty="No officials have posted in this window yet."
                isEmpty={offs.length === 0}
            >
                {offs.map(renderCard)}
            </ThreeWayColumn>
            <ThreeWayColumn
                header="The Public"
                byline={`Political subreddits, plus X users we don't track individually${topicSuffix}`}
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
    item, onClose, activeTopic,
}: {
    item: EntitySentimentItem;
    onClose: () => void;
    activeTopic: Topic;
}) {
    const { entityProfile: profile, netScore, volume, classificationSamples } = item;
    const sourceUrl = entityExternalUrl(profile);

    // When a topic is active, filter the visible classification samples
    // client-side. We don't have entity-scoped-by-topic scores from the
    // backend (API gap), so the headline net score remains the entity's
    // global score — the topic strip is explicit about that.
    const allSamples: ClassificationSample[] = classificationSamples ?? [];
    const filteredSamples = activeTopic.key === 'all'
        ? allSamples
        : allSamples.filter(s => matchesTopic(activeTopic, s.title, s.full_text, ...s.evidence_spans));
    const samplesAreFiltered = activeTopic.key !== 'all';

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle={buildEntitySubtitle(profile)}
            accentColor={entityLeanAccent(profile)}
        >
            {samplesAreFiltered && (
                <TopicScopeStrip
                    activeTopic={activeTopic}
                    matched={filteredSamples.length}
                    total={allSamples.length}
                />
            )}

            <EntityHeader profile={profile} />

            <div className="entity-modal-stats">
                <div>
                    <div
                        className="eyebrow"
                        title="Positive minus negative share of this source's posts, from -100 (all negative) to +100 (all positive)."
                    >
                        Net tone
                    </div>
                    <div className="metric-value">
                        {formatPts(netScore)}
                    </div>
                    {samplesAreFiltered && (
                        <div className="text-xs text-muted">
                            (across all topics — {activeTopic.label}-only score not yet available)
                        </div>
                    )}
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
                            Political lean rated by: {profile.leanSource}
                        </span>
                    )}
                    {profile.kind === 'official' && profile.bioSource && (
                        <a href={profile.bioSource} target="_blank" rel="noreferrer">Bio ↗</a>
                    )}
                </div>
            )}

            {filteredSamples.length > 0 ? (
                <>
                    <h3 className="card-title mt-4 mb-2">
                        {samplesAreFiltered
                            ? `Recent posts matching ${activeTopic.label}`
                            : 'Recent classified posts'}
                    </h3>
                    <SupportingDocsTable
                        docs={filteredSamples.map(classificationSampleToSupportingDoc)}
                    />
                </>
            ) : samplesAreFiltered ? (
                <p className="text-muted text-sm mt-4">
                    None of this entity's recent classified posts match {activeTopic.label}.
                </p>
            ) : null}
        </Modal>
    );
}

function TopicScopeStrip({
    activeTopic, matched, total,
}: {
    activeTopic: Topic;
    matched: number;
    total: number;
}) {
    return (
        <div
            className="modal-topic-strip"
            role="status"
            aria-label={`Filtered to topic: ${activeTopic.label}`}
        >
            <span className="modal-topic-strip-icon">
                <svg
                    viewBox="0 0 24 24"
                    width={14}
                    height={14}
                    fill="none"
                    stroke="currentColor"
                    strokeWidth={1.75}
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    aria-hidden
                >
                    {activeTopic.iconPaths.map((d, i) => <path key={i} d={d} />)}
                </svg>
            </span>
            <span className="modal-topic-strip-label">Showing: {activeTopic.label}</span>
            <span className="modal-topic-strip-detail">
                · {matched} of {total} recent posts match this topic
            </span>
        </div>
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
        <CollapsibleInfo summary="Online stance vs. recent polling">
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
                        <div className="eyebrow">Recent polling</div>
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
                We aggregate news articles, Reddit posts, and X posts about US politics from the
                last 30 days, then score each one for tone (positive / negative / neutral), and
                each score must point to the exact sentence that justifies it. Tracked outlets
                and officials each get their own profile card; everything else rolls up into the
                general-public column or into an "Other" bucket for sources we don't track
                individually.
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
//  Topic state — URL persistence + default selection                          //
// --------------------------------------------------------------------------- //

const TOPIC_QS_KEY = 'topic';

function readTopicFromUrl(): TopicKey {
    try {
        const params = new URLSearchParams(window.location.search);
        const slug = params.get(TOPIC_QS_KEY);
        return topicFromSlug(slug).key;
    } catch {
        return 'all';
    }
}

function writeTopicToUrl(key: TopicKey): void {
    try {
        const topic = topicByKey(key);
        const params = new URLSearchParams(window.location.search);
        if (topic.key === 'all') {
            params.delete(TOPIC_QS_KEY);
        } else {
            params.set(TOPIC_QS_KEY, topic.slug);
        }
        const qs = params.toString();
        const url =
            window.location.pathname + (qs ? `?${qs}` : '') + window.location.hash;
        window.history.replaceState({}, '', url);
    } catch { /* noop — older browsers / sandboxed contexts */ }
}

function pickDefaultTopic(byTopic: SentimentBreakdown[] | undefined): TopicKey {
    // First load behavior: pick the topic with the most volume in the
    // current window so the page lands on something substantive instead
    // of an arbitrary alphabetical default. Falls back to 'all' if no
    // per-topic data is available.
    if (!byTopic || byTopic.length === 0) return 'all';
    const validTopicNames = new Set(TOPICS.filter(t => t.key !== 'all').map(t => t.key));
    let best: { key: TopicKey; volume: number } | null = null;
    for (const row of byTopic) {
        if (!row.topic || !validTopicNames.has(row.topic as TopicKey)) continue;
        const volume = row.volume ?? 0;
        if (!best || volume > best.volume) {
            best = { key: row.topic as TopicKey, volume };
        }
    }
    return best && best.volume > 0 ? best.key : 'all';
}


// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

function netToneColor(net: number): TickerItem['tone'] {
    // Green for positive so it matches the tier-row tone color (toneColor);
    // a blue "accent" here previously made the same positive reading render
    // in two different hues on the same page.
    if (net > 10) return 'positive';
    if (net < -10) return 'negative';
    return 'neutral';
}

function buildSentimentTickerItems(data: PublicSentimentData, activeTopic: Topic): TickerItem[] {
    const overall = data.overview;
    // The ticker always reads the site-wide sentiment rollup; the backend
    // doesn't expose these figures scoped to a single topic. When a topic
    // filter is active we mark each item "all topics" so a reader inside a
    // filtered view doesn't read a global number as a topic number (R-2).
    const scopeHint = activeTopic.key === 'all' ? undefined : 'all topics';
    const items: TickerItem[] = [
        {
            label: 'Net tone',
            value: formatPts(overall.netScore),
            hint: scopeHint,
            tone: netToneColor(overall.netScore),
            emphasis: true,
            ariaLabel: `Net tone ${formatPts(overall.netScore)}`,
        },
    ];
    if (data.gopFavorability) {
        const gopNet = data.gopFavorability.netFavorability;
        items.push({
            label: 'Tone toward GOP',
            value: formatPts(gopNet),
            hint: scopeHint,
            tone: netToneColor(gopNet),
            emphasis: true,
            ariaLabel: `Tone toward GOP ${formatPts(gopNet)}`,
        });
    }
    items.push(
        { label: 'Posts scored', value: overall.volume.toLocaleString(), hint: scopeHint },
    );
    return items;
}

/**
 * Static framing sentence for the Overall Tone page. When a topic is
 * active, the spec calls for "How news outlets, public officials, and
 * everyday people are feeling and talking about [TOPIC]." The unfiltered
 * default keeps the original sentence.
 */
function readsAsToday(activeTopic: Topic): string {
    if (activeTopic.key === 'all') {
        return 'How news outlets, public officials, and everyday people are reading American politics.';
    }
    return `How news outlets, public officials, and everyday people are feeling and talking about ${activeTopic.label}.`;
}


interface PublicSentimentProps {
    filters: Filters;
}

function PublicSentiment({ filters }: PublicSentimentProps) {
    const [activeEntity, setActiveEntity] = useState<EntitySentimentItem | null>(null);
    const [activeTopicKey, setActiveTopicKeyState] = useState<TopicKey>(() => readTopicFromUrl());
    // Tracks whether the current activeTopicKey came from the URL (or
    // explicit user click) vs. the implicit default. We only auto-pick a
    // most-discussed default on the first render where no URL value was
    // present — re-fetches on filter changes shouldn't keep flipping the
    // user's selection.
    const [pickedDefault, setPickedDefault] = useState<boolean>(() => readTopicFromUrl() !== 'all');

    const setActiveTopicKey = (key: TopicKey) => {
        setActiveTopicKeyState(key);
        setPickedDefault(true);
        writeTopicToUrl(key);
    };

    const { data, loading, error, refetch } = useFetch<PublicSentimentData>(
        async () => transformPublicSentiment(await fetchSentiment(filters.timeRange)),
        [filters.timeRange],
        `sentiment:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatus>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );

    // Pick the most-discussed topic as the default once data lands, but
    // only if the user hasn't already chosen something (URL or click).
    useEffect(() => {
        if (pickedDefault) return;
        if (!data) return;
        const def = pickDefaultTopic(data.byTopic);
        if (def !== 'all') {
            setActiveTopicKeyState(def);
            writeTopicToUrl(def);
            setPickedDefault(true);
        }
    }, [data, pickedDefault]);

    // Sync state from popstate/back-forward navigation that mutates the
    // ?topic= query param while staying on the page.
    useEffect(() => {
        const onPop = () => {
            const next = readTopicFromUrl();
            setActiveTopicKeyState(next);
            setPickedDefault(next !== 'all');
        };
        window.addEventListener('popstate', onPop);
        return () => window.removeEventListener('popstate', onPop);
    }, []);

    const activeTopic = topicByKey(activeTopicKey);
    const topicRow = useMemo<SentimentBreakdown | null>(() => {
        if (!data || activeTopic.key === 'all') return null;
        return data.byTopic.find(t => t.topic === activeTopic.key) ?? null;
    }, [data, activeTopic.key]);

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

    const tickerItems = buildSentimentTickerItems(data, activeTopic);
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
                    legend={
                        <MethodPopover
                            title="How to read these numbers"
                            description={
                                'Net tone = the share of sampled posts scored positive minus the share scored '
                                + 'negative, in points on a -100 to +100 scale (0 means positive and negative '
                                + 'balance out). Tone toward GOP = the net stance of sampled posts toward '
                                + 'Republican-party entities, on the same scale. Both summarize the posts we '
                                + 'collected — they are samples, not polls of the public.'
                            }
                        />
                    }
                />
            </div>

            {/* Topic tab bar — visual anchor of the page (spec A2). */}
            <div className="col-span-12">
                <TopicTabBar
                    activeKey={activeTopicKey}
                    onChange={setActiveTopicKey}
                    byTopic={data.byTopic}
                />
            </div>

            <div className="col-span-12">
                <div className="reads-as-today">
                    <span className="eyebrow reads-as-today-eyebrow">
                        {asOfTodayEyebrow(filters.timeRange)}
                    </span>
                    <p className="lead" style={{ margin: 0 }}>{readsAsToday(activeTopic)}</p>
                </div>
            </div>

            {/* Compact top-metrics block — tier tones (topic-scoped when filtered)
                + GOP + intensity. */}
            <div className="col-span-12">
                <TopMetrics
                    data={data}
                    windowLabel={formatTimeWindow(filters.timeRange)}
                    activeTopic={activeTopic}
                    topicRow={topicRow}
                />
            </div>

            {/* Three-way grid: News / Officials / Public. */}
            <div className="col-span-12">
                <SentimentThreeWayGrid
                    newsOutlets={data.byNewsOutlet ?? []}
                    officials={data.byOfficial ?? []}
                    generalPublic={data.byGeneralPublic ?? []}
                    onOpen={setActiveEntity}
                    activeTopic={activeTopic}
                />
            </div>

            {activeEntity && (
                <EntitySentimentModal
                    item={activeEntity}
                    onClose={() => setActiveEntity(null)}
                    activeTopic={activeTopic}
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
