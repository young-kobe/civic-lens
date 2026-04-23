import { useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, EntityHeader, EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, Modal,
    ThreeWayColumn, ThreeWayGrid,
    TierRow, TopMetricsBlock, entityExternalUrl, entityLeanAccent,
} from '../components/common';
import type { TickerItem, TierRowDot } from '../components/common';
import { fetchPropaganda, fetchSnapshotStatus, type SnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { useFetch } from '../services/useFetch';
import { formatRefreshedAgo, getSnapshotTimestamp } from '../services/freshness';
import { COLORS } from '../theme';
import type {
    Filters, PropagandaEntityItem, PropagandaExample,
    PropagandaOverview, PropagandaSourceSplit, PropagandaTechniqueCount,
    PropagandaTechniqueName,
} from '../types';


// --------------------------------------------------------------------------- //
//  Dictionaries                                                               //
// --------------------------------------------------------------------------- //

const TECHNIQUE_LABEL: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Loaded language',
    name_calling: 'Name-calling',
    ad_hominem: 'Ad hominem',
    appeal_to_fear: 'Appeal to fear',
    whataboutism: 'Whataboutism',
    doubt_casting: 'Doubt-casting',
};

const TECHNIQUE_BLURB: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Emotionally charged framing designed to influence rather than inform.',
    name_calling: 'Dismissive labels applied to a person or group instead of engaging their argument.',
    ad_hominem: "Attacks on the speaker's character rather than the substance of what they said.",
    appeal_to_fear: 'Fear or catastrophic imagery used to bypass reasoning.',
    whataboutism: 'Deflecting criticism by pointing at an unrelated alleged misconduct by the other side.',
    doubt_casting: 'Insinuation of wrongdoing or unreliability without offering evidence.',
};


// --------------------------------------------------------------------------- //
//  Top metrics block — compact Bloomberg-style                                //
// --------------------------------------------------------------------------- //

function PropagandaTopMetrics({
    data,
    windowLabel,
}: {
    data: PropagandaOverview;
    windowLabel: string;
}) {
    const newsSplit = data.by_source.find((s) => s.label === 'News');
    const socialSplit = data.by_source.find((s) => s.label === 'Social Media');
    const gapPct = newsSplit && socialSplit
        ? Math.abs(newsSplit.flagged_rate_pct - socialSplit.flagged_rate_pct)
        : 0;
    const leanMoreFlagged = !newsSplit || !socialSplit
        ? 'neither'
        : newsSplit.flagged_rate_pct > socialSplit.flagged_rate_pct
            ? 'news'
            : 'social media';
    const topTech = data.by_technique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : '—';

    const flaggedDotColor = data.propaganda_rate_pct > 20
        ? COLORS.negative
        : data.propaganda_rate_pct > 10
            ? COLORS.warning
            : COLORS.positive;

    const splitDots: TierRowDot[] = [];
    if (newsSplit) splitDots.push({
        pct: newsSplit.flagged_rate_pct,
        color: 'var(--neutral-600)',
        title: `News ${newsSplit.flagged_rate_pct.toFixed(1)}%`,
    });
    if (socialSplit) splitDots.push({
        pct: socialSplit.flagged_rate_pct,
        color: COLORS.warning,
        title: `Social ${socialSplit.flagged_rate_pct.toFixed(1)}%`,
    });

    return (
        <TopMetricsBlock
            eyebrow={`As of ${windowLabel}`}
            meta={`${data.total_eligible_docs.toLocaleString()} scored posts`}
        >
            <TierRow
                label="Flagged rate"
                value={`${data.propaganda_rate_pct.toFixed(1)}%`}
                verb={`${data.flagged_docs.toLocaleString()} flagged · mean score ${data.mean_score.toFixed(2)}`}
                dotPct={data.propaganda_rate_pct}
                dotColor={flaggedDotColor}
            />
            <TierRow
                label="News vs social"
                value={gapPct > 0 ? `${gapPct.toFixed(1)} pts` : '—'}
                verb={leanMoreFlagged === 'neither'
                    ? 'no data'
                    : `${leanMoreFlagged} uses more techniques`}
                dots={splitDots}
            />
            <TierRow
                label="Top technique"
                value={topTech ? `${topTech.pct_of_flagged_docs.toFixed(0)}%` : '—'}
                verb={topTech
                    ? `${topTechLabel} · in ${topTech.count.toLocaleString()} flagged posts`
                    : topTechLabel}
                dotPct={topTech?.pct_of_flagged_docs}
                dotColor={topTech ? COLORS.negative : undefined}
            />
        </TopMetricsBlock>
    );
}


// --------------------------------------------------------------------------- //
//  Ticker + reads-as-today                                                    //
// --------------------------------------------------------------------------- //

function buildPropagandaTickerItems(data: PropagandaOverview): TickerItem[] {
    const topTech = data.by_technique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : '—';
    const rate = data.propaganda_rate_pct;
    return [
        {
            label: 'Flagged rate',
            value: `${rate.toFixed(1)}%`,
            tone: rate > 20 ? 'negative' : rate > 10 ? 'accent' : 'positive',
            emphasis: true,
            ariaLabel: `Flagged rate ${rate.toFixed(1)} percent`,
        },
        {
            label: 'Flagged posts',
            value: data.flagged_docs.toLocaleString(),
            hint: `of ${data.total_eligible_docs.toLocaleString()}`,
        },
        {
            label: 'Mean score',
            value: data.mean_score.toFixed(2),
        },
        {
            label: 'Top technique',
            value: topTechLabel,
            hint: topTech ? `${topTech.pct_of_flagged_docs.toFixed(0)}% of flagged` : undefined,
        },
    ];
}

/** Template-derived headline naming whichever side (news or social) is
 *  using more techniques, plus the single most prevalent technique. */
function readsAsToday(data: PropagandaOverview): string {
    const news = data.by_source.find((s) => s.label === 'News');
    const social = data.by_source.find((s) => s.label === 'Social Media');
    const topTech = data.by_technique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : null;

    const parts: string[] = [];
    if (news && social) {
        const gap = news.flagged_rate_pct - social.flagged_rate_pct;
        if (Math.abs(gap) < 2) {
            parts.push('News and social media use these techniques at similar rates.');
        } else if (gap > 0) {
            parts.push(`News leans on these techniques more than social media (${news.flagged_rate_pct.toFixed(1)}% vs ${social.flagged_rate_pct.toFixed(1)}% flagged).`);
        } else {
            parts.push(`Social media leans on these techniques more than news (${social.flagged_rate_pct.toFixed(1)}% vs ${news.flagged_rate_pct.toFixed(1)}% flagged).`);
        }
    }
    if (topTechLabel && topTech) {
        parts.push(`${topTechLabel} is the most common, appearing in ${topTech.pct_of_flagged_docs.toFixed(0)}% of flagged posts.`);
    }
    return parts.length > 0
        ? parts.join(' ')
        : 'No flagged posts in this window.';
}


// --------------------------------------------------------------------------- //
//  Propaganda entity card — wraps the shared EntityProfileCard with            //
//  propaganda-specific stats (flagged rate / mean score / posts scored).      //
// --------------------------------------------------------------------------- //

function PropagandaEntityCard({
    item, onOpen,
}: {
    item: PropagandaEntityItem;
    onOpen: (item: PropagandaEntityItem) => void;
}) {
    const profile = item.entity_profile;
    const rateColor = item.flagged_rate_pct > 25
        ? COLORS.negative
        : item.flagged_rate_pct > 10
            ? COLORS.warning
            : 'var(--neutral-700)';

    return (
        <EntityProfileCard
            profile={profile}
            stats={item.total_docs > 0 ? [
                { label: 'Flagged',      value: `${item.flagged_rate_pct.toFixed(1)}%`, color: rateColor, emphasis: true },
                { label: 'Mean score',   value: item.mean_score.toFixed(2) },
                { label: 'Posts scored', value: item.total_docs.toLocaleString() },
            ] : []}
            onClick={item.total_docs > 0 ? () => onOpen(item) : undefined}
            href={item.total_docs > 0 ? undefined : entityExternalUrl(profile) ?? undefined}
            emptyNote="Tracked — no scored posts in this window yet."
        />
    );
}


// --------------------------------------------------------------------------- //
//  Per-entity detail modal — filters the overall examples list down to just  //
//  the one outlet / official / subreddit the reader clicked.                 //
// --------------------------------------------------------------------------- //

function entityMatchesExample(item: PropagandaEntityItem, ex: PropagandaExample): boolean {
    if (item.kind === 'outlet') {
        return ex.source_type === 'news'
            && (ex.domain ?? '').toLowerCase().replace(/^www\./, '')
               === item.key.toLowerCase().replace(/^www\./, '');
    }
    if (item.kind === 'official') {
        return ex.source_type === 'x_post'
            && (ex.author_handle ?? '').toLowerCase() === item.key.toLowerCase();
    }
    if (item.kind === 'subreddit') {
        return (ex.source_type === 'reddit_post' || ex.source_type === 'reddit_comment')
            && (ex.domain ?? '').toLowerCase() === item.key.toLowerCase();
    }
    // catch-all buckets don't correspond to a single entity's examples.
    return false;
}

function PropagandaEntityModal({
    item, examples, onClose,
}: {
    item: PropagandaEntityItem;
    examples: PropagandaExample[];
    onClose: () => void;
}) {
    const profile = item.entity_profile;
    const rateColor = item.flagged_rate_pct > 25
        ? COLORS.negative
        : item.flagged_rate_pct > 10
            ? COLORS.warning
            : 'var(--neutral-700)';
    const sourceUrl = entityExternalUrl(profile);
    const matching = examples.filter((ex) => entityMatchesExample(item, ex));

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle={`${item.flagged_docs.toLocaleString()} flagged · ${item.total_docs.toLocaleString()} scored`}
            accentColor={entityLeanAccent(profile)}
        >
            <EntityHeader profile={profile} />

            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow">Flagged rate</div>
                    <div className="metric-value" style={{ color: rateColor }}>
                        {item.flagged_rate_pct.toFixed(1)}%
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Mean score</div>
                    <div className="metric-value">{item.mean_score.toFixed(2)}</div>
                </div>
                <div>
                    <div className="eyebrow">Posts scored</div>
                    <div className="metric-value">{item.total_docs.toLocaleString()}</div>
                </div>
            </div>

            {(sourceUrl || profile.leanSource) && (
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
                </div>
            )}

            <h3 className="card-title mt-4 mb-2">
                {matching.length === 0 ? 'No flagged examples in this window'
                    : matching.length === 1 ? 'Flagged example'
                    : `Flagged examples (${matching.length})`}
            </h3>
            {matching.length === 0 ? (
                <p className="text-muted text-sm">
                    This entity has a score above but none of the recent flagged examples we fetched
                    for the Examples card come from it. Try widening the time window.
                </p>
            ) : (
                <div className="example-rows">
                    {matching.map((ex) => <ExampleRow key={ex.doc_id} ex={ex} />)}
                </div>
            )}
        </Modal>
    );
}


// --------------------------------------------------------------------------- //
//  Three-way grid — uses the by_news_outlet / by_official / by_general_public //
// --------------------------------------------------------------------------- //

const TOP_N = 12;

function ThreeWayEntityGrid({
    data, onOpen,
}: {
    data: PropagandaOverview;
    onOpen: (item: PropagandaEntityItem) => void;
}) {
    const outlets = (data.by_news_outlet ?? []).slice(0, TOP_N);
    const officials = (data.by_official ?? []).slice(0, TOP_N);
    const publics = (data.by_general_public ?? []).slice(0, TOP_N);
    const renderCard = (it: PropagandaEntityItem) => (
        <PropagandaEntityCard key={it.key} item={it} onOpen={onOpen} />
    );

    return (
        <ThreeWayGrid>
            <ThreeWayColumn
                header="The News"
                byline="News outlets sorted by how heavily their posts use these techniques"
                empty="No news articles scored yet."
                isEmpty={outlets.length === 0}
            >
                {outlets.map(renderCard)}
            </ThreeWayColumn>
            <ThreeWayColumn
                header="Politicians & Officials"
                byline="Tracked officeholders, sorted by mean propaganda score"
                empty="No officials scored yet."
                isEmpty={officials.length === 0}
            >
                {officials.map(renderCard)}
            </ThreeWayColumn>
            <ThreeWayColumn
                header="The Public"
                byline="Subreddits + the broader X user catch-all"
                empty="No social posts scored yet."
                isEmpty={publics.length === 0}
            >
                {publics.map(renderCard)}
            </ThreeWayColumn>
        </ThreeWayGrid>
    );
}


// --------------------------------------------------------------------------- //
//  Techniques Used                                                            //
// --------------------------------------------------------------------------- //

function TechniquesCard({ techniques }: { techniques: PropagandaTechniqueCount[] }) {
    const maxCount = techniques.reduce((m, t) => Math.max(m, t.count), 0);

    return (
        <Card
            title="Techniques being used"
            subtitle="One post can count toward multiple techniques. Each is detected with a verbatim evidence span from the source."
        >
            <div className="technique-rows">
                {techniques.map((t) => {
                    const name = t.technique as PropagandaTechniqueName;
                    const label = TECHNIQUE_LABEL[name] || name;
                    const blurb = TECHNIQUE_BLURB[name] || '';
                    const widthPct = maxCount > 0 ? (t.count / maxCount) * 100 : 0;
                    return (
                        <div key={t.technique} className="technique-row">
                            <div className="technique-row-label">
                                <div className="technique-row-name">{label}</div>
                                <div className="technique-row-blurb">{blurb}</div>
                            </div>
                            <div className="technique-row-bar">
                                <div
                                    className="technique-row-bar-fill"
                                    style={{ width: `${widthPct}%` }}
                                />
                            </div>
                            <div className="technique-row-count">
                                {t.count.toLocaleString()}
                                <span className="technique-row-count-pct">
                                    {t.pct_of_flagged_docs.toFixed(0)}% of flagged
                                </span>
                            </div>
                        </div>
                    );
                })}
            </div>
        </Card>
    );
}


// --------------------------------------------------------------------------- //
//  News vs Social split                                                       //
// --------------------------------------------------------------------------- //

const SPLIT_DOT_COLOR: Record<string, string> = {
    'News':         'var(--neutral-600)',
    'Social Media': COLORS.warning,
};

function NewsVsSocialCard({ splits }: { splits: PropagandaSourceSplit[] }) {
    return (
        <Card
            title="News vs. social media"
            subtitle="Flagged rate and mean score by source bucket"
        >
            <div className="source-split-rows">
                {splits.map((s) => (
                    <div key={s.label} className="source-split-row">
                        <span className="source-split-row-label">
                            <span
                                className="source-split-dot"
                                style={{ background: SPLIT_DOT_COLOR[s.label] ?? 'var(--neutral-500)' }}
                                aria-hidden
                            />
                            {s.label}
                        </span>
                        <span className="source-split-row-total">
                            {s.total_docs.toLocaleString()} posts
                        </span>
                        <span className="source-split-row-rate">
                            {s.flagged_rate_pct.toFixed(1)}%
                            <span className="source-split-row-sub">flagged</span>
                        </span>
                        <span className="source-split-row-score">
                            {s.mean_score.toFixed(2)}
                            <span className="source-split-row-sub">mean score</span>
                        </span>
                    </div>
                ))}
            </div>
        </Card>
    );
}


// --------------------------------------------------------------------------- //
//  Examples                                                                   //
// --------------------------------------------------------------------------- //

function ExampleRow({ ex }: { ex: PropagandaExample }) {
    const sourceMeta = ex.source_type === 'x_post' && ex.author_handle
        ? `X · @${ex.author_handle}`
        : `${ex.source_type} · ${ex.domain || 'unknown'}`;
    return (
        <div className="example-row">
            <div className="example-row-head">
                <span className="example-row-meta">
                    {sourceMeta} · doc #{ex.doc_id}
                </span>
                <span className="example-row-score">
                    score {ex.overall_score.toFixed(2)}
                </span>
                {ex.url && (
                    <a
                        href={ex.url}
                        target="_blank"
                        rel="noreferrer"
                        className="example-row-link"
                        aria-label={`Open source for doc ${ex.doc_id} in new tab`}
                    >
                        View original ↗
                    </a>
                )}
            </div>
            {ex.title && <div className="example-row-title">{ex.title}</div>}
            <div className="example-row-preview">
                {ex.text_preview}
                {ex.text_preview.length >= 240 ? '…' : ''}
            </div>
            <div className="example-row-techs">
                {ex.techniques.map((t, i) => (
                    <span
                        key={i}
                        className="example-tech"
                        title={`confidence ${t.confidence.toFixed(2)}`}
                    >
                        <strong>
                            {TECHNIQUE_LABEL[t.technique as PropagandaTechniqueName] || t.technique}
                        </strong>
                        {': '}
                        <em>"{t.evidence_span}"</em>
                    </span>
                ))}
            </div>
        </div>
    );
}

function ExamplesCard({ examples }: { examples: PropagandaExample[] }) {
    if (examples.length === 0) return null;
    return (
        <Card
            title="Recent flagged posts"
            subtitle="Each flag comes with a verbatim evidence quote from the source."
        >
            <div className="example-rows">
                {examples.map((ex) => <ExampleRow key={ex.doc_id} ex={ex} />)}
            </div>
        </Card>
    );
}


// --------------------------------------------------------------------------- //
//  How this works                                                             //
// --------------------------------------------------------------------------- //

function HowThisWorks() {
    return (
        <CollapsibleInfo>
            <p className="text-sm">
                Each post is scored for six rhetorical techniques: loaded language, name-calling,
                ad hominem, appeal to fear, whataboutism, and doubt-casting. The model has to
                quote a verbatim phrase from the source as evidence, so flags aren't hand-waved.
            </p>
            <p className="text-sm">
                A high flagged rate means the sample leaned on these techniques — it's a measure
                of technique density, not authorial intent. Straight reporting reads as unflagged;
                opinion and activist content tends to light up.
            </p>
        </CollapsibleInfo>
    );
}


// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

interface PropagandaProps {
    filters: Filters;
}

function Propaganda({ filters }: PropagandaProps) {
    const [activeEntity, setActiveEntity] = useState<PropagandaEntityItem | null>(null);
    const { data, loading, error, refetch } = useFetch<PropagandaOverview>(
        () => fetchPropaganda(filters.timeRange, filters.sourceType),
        [filters.timeRange, filters.sourceType],
        `propaganda:${filters.timeRange}:${filters.sourceType}`,
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
                <LoadingCard />
            </div>
        );
    }
    // Early-return only when the fetch itself yielded nothing. Zero flagged
    // docs is a legitimate state that the page should render honestly —
    // GlobalTicker + three-way grid with per-column empty copy, same as
    // Tone and Narratives do. Hiding the frame on empty data was
    // misleading: a stalled propaganda-detection cron looked identical to
    // a clean run with nothing to flag.
    if (!data) return <EmptyState title="No propaganda data available" />;

    const windowLabel = formatTimeWindow(filters.timeRange);
    const hasEntityData =
        (data.by_news_outlet?.length ?? 0) +
        (data.by_official?.length ?? 0) +
        (data.by_general_public?.length ?? 0) > 0;

    const tickerItems = buildPropagandaTickerItems(data);
    const refreshed = formatRefreshedAgo(
        getSnapshotTimestamp(snapshotStatus, `propaganda_${filters.timeRange}`),
    );

    return (
        <>
            <div className="dashboard-grid">
                <div className="col-span-12">
                    <GlobalTicker
                        items={tickerItems}
                        refreshed={refreshed}
                        ariaLabel="Propaganda overview"
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
                    <PropagandaTopMetrics data={data} windowLabel={windowLabel} />
                </div>

                {hasEntityData && (
                    <div className="col-span-12">
                        <ThreeWayEntityGrid data={data} onOpen={setActiveEntity} />
                    </div>
                )}

                <div className="col-span-7">
                    <TechniquesCard techniques={data.by_technique} />
                </div>

                <div className="col-span-5">
                    <NewsVsSocialCard splits={data.by_source} />
                </div>

                <div className="col-span-12">
                    <ExamplesCard examples={data.examples} />
                </div>

                <div className="col-span-12">
                    <HowThisWorks />
                </div>
            </div>

            {activeEntity && (
                <PropagandaEntityModal
                    item={activeEntity}
                    examples={data.examples}
                    onClose={() => setActiveEntity(null)}
                />
            )}
        </>
    );
}

export default Propaganda;
