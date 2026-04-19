import { useState, useEffect } from 'react';
import { Card, ConfidenceBadge, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { SentimentBar, TrendStrip } from '../components/charts';
import type { Filters, PublicSentimentData, SentimentOverview, SentimentBreakdown, SentimentDistribution, SocialVsNewsSentiment, PollingSocialComparison, TrendPoint, ClassificationSample } from '../types';

import { fetchSentiment } from '../services/api';
import { transformPublicSentiment } from '../services/transformers';

const SENTIMENT_COLORS = {
    positive: '#008a4c',
    negative: '#d41e0e',
    neutral: '#8e8e96',
};

const LABEL_BADGE_STYLES: Record<string, { bg: string; text: string }> = {
    POSITIVE: { bg: '#e3f6eb', text: '#008a4c' },
    NEGATIVE: { bg: '#fbe7e4', text: '#d41e0e' },
    NEUTRAL: { bg: '#f4f4f5', text: '#45454d' },
    MIXED: { bg: '#fdf0dd', text: '#b26100' },
};


interface SentimentOverviewHeaderProps {
    data: SentimentOverview;
}

function SentimentOverviewHeader({ data }: SentimentOverviewHeaderProps) {
    const getScoreDisplay = (score: number) => {
        if (score > 0.1) return { label: 'Positive', class: 'metric-delta-positive' };
        if (score < -0.1) return { label: 'Negative', class: 'metric-delta-negative' };
        return { label: 'Neutral', class: 'metric-delta-neutral' };
    };

    const scoreInfo = getScoreDisplay(data.netScore);

    return (
        <Card className="mb-4">
            <div className="flex items-start justify-between">
                <div>
                    <div className="eyebrow mb-2">Net Sentiment Score</div>
                    <div className="flex items-baseline gap-3">
                        <span className={`metric-value-lg ${scoreInfo.class}`}>
                            {data.netScore >= 0 ? '+' : ''}{data.netScore.toFixed(1)}%
                        </span>
                        <span className={`text-sm font-bold uppercase ${scoreInfo.class}`}>
                            {scoreInfo.label}
                        </span>
                    </div>
                </div>
                <div className="text-right">
                    <div className="eyebrow mb-2">Total Volume</div>
                    <div className="num" style={{ fontSize: 'var(--text-2xl)', fontWeight: 600, letterSpacing: '-0.02em' }}>
                        {data.volume.toLocaleString()}
                    </div>
                </div>
            </div>
            <div className="mt-3 pt-3" style={{ borderTop: '1px solid var(--neutral-200)' }}>
                <ConfidenceBadge
                    coverage={data.coverage}
                    confidence={data.confidence}
                    sampleSize={data.volume}
                />
            </div>
        </Card>
    );
}


/* ------------------------------------------------------------------ */
/*  Mini Donut Chart (CSS-only conic gradient)                        */
/* ------------------------------------------------------------------ */

interface MiniDonutProps {
    positive: number;
    negative: number;
    neutral: number;
    size?: number;
}

function MiniDonut({ positive, negative, neutral, size = 56 }: MiniDonutProps) {
    const total = positive + negative + neutral || 1;
    const negPct = (negative / total) * 100;
    const neuPct = (neutral / total) * 100;
    const posPct = (positive / total) * 100;

    const gradient = `conic-gradient(
        ${SENTIMENT_COLORS.negative} 0% ${negPct}%,
        ${SENTIMENT_COLORS.neutral} ${negPct}% ${negPct + neuPct}%,
        ${SENTIMENT_COLORS.positive} ${negPct + neuPct}% 100%
    )`;

    return (
        <div style={{
            width: size, height: size, borderRadius: '50%',
            background: gradient, position: 'relative', flexShrink: 0,
        }}>
            <div style={{
                position: 'absolute', inset: size * 0.22,
                borderRadius: '50%', background: 'var(--bg-card, #fff)',
            }} />
            <div style={{
                position: 'absolute', inset: 0, display: 'flex',
                alignItems: 'center', justifyContent: 'center',
                fontSize: '11px', fontWeight: 600, color: 'var(--text-primary)',
            }}>
                {posPct.toFixed(0)}%
            </div>
        </div>
    );
}


/* ------------------------------------------------------------------ */
/*  Single Topic Row (expandable)                                     */
/* ------------------------------------------------------------------ */

interface TopicRowProps {
    item: SentimentBreakdown;
}

function TopicRow({ item }: TopicRowProps) {
    const [expanded, setExpanded] = useState(false);
    const total = item.positive + item.negative + item.neutral || 1;
    const netScore = ((item.positive - item.negative) / total * 100);
    const netColor = netScore > 5 ? SENTIMENT_COLORS.positive
        : netScore < -5 ? SENTIMENT_COLORS.negative
        : SENTIMENT_COLORS.neutral;
    const hasSamples = (item.classificationSamples?.length ?? 0) > 0;

    return (
        <div style={{
            background: 'var(--bg-card)', borderRadius: 'var(--radius-sm)',
            border: '1px solid var(--neutral-200)',
        }}>
            {/* Header row */}
            <button
                onClick={() => hasSamples && setExpanded(!expanded)}
                style={{
                    display: 'flex', alignItems: 'center', gap: '14px',
                    width: '100%', padding: '10px 12px', border: 'none',
                    background: 'transparent', cursor: hasSamples ? 'pointer' : 'default',
                    textAlign: 'left',
                }}
                id={`topic-row-${item.topic?.replace(/\s/g, '-').toLowerCase()}`}
            >
                <MiniDonut positive={item.positive} negative={item.negative} neutral={item.neutral} size={44} />

                <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                        <span style={{ fontWeight: 700, fontSize: '13px', textTransform: 'uppercase', letterSpacing: '0.03em' }}>{item.topic}</span>
                        <span className="num" style={{
                            fontSize: '11px', fontWeight: 700, padding: '1px 6px',
                            borderRadius: '2px', color: netColor,
                            background: netColor === SENTIMENT_COLORS.positive ? 'var(--semantic-positive-light)'
                                : netColor === SENTIMENT_COLORS.negative ? 'var(--semantic-negative-light)'
                                : 'var(--neutral-100)',
                        }}>
                            {netScore >= 0 ? '+' : ''}{netScore.toFixed(1)}%
                        </span>
                        {(item.sarcasm_rate ?? 0) > 0 && (
                            <span className="badge badge-warning num">
                                {item.sarcasm_rate?.toFixed(0)}% SARC
                            </span>
                        )}
                    </div>
                    <div style={{ display: 'flex', gap: '14px', fontSize: '11px', color: 'var(--neutral-500)', fontFamily: 'var(--font-mono)', fontVariantNumeric: 'tabular-nums' }}>
                        <span>{item.volume.toLocaleString()} docs</span>
                        <span style={{ color: SENTIMENT_COLORS.positive }}>
                            +{((item.positive / total) * 100).toFixed(0)}% pos
                        </span>
                        <span style={{ color: SENTIMENT_COLORS.negative }}>
                            -{((item.negative / total) * 100).toFixed(0)}% neg
                        </span>
                    </div>
                </div>

                {hasSamples && (
                    <span style={{
                        fontSize: '14px', color: 'var(--neutral-500)',
                        transform: expanded ? 'rotate(180deg)' : 'rotate(0deg)',
                        transition: 'transform 0.2s ease', lineHeight: 1,
                        fontFamily: 'var(--font-mono)',
                    }}>
                        v
                    </span>
                )}
            </button>

            {/* Expanded reasoning panel */}
            {expanded && hasSamples && (
                <div style={{
                    padding: '0 12px 12px 12px',
                    borderTop: '1px solid var(--neutral-200)',
                }}>
                    <div className="eyebrow" style={{ padding: '10px 0 8px' }}>
                        Classification Reasoning · Top {item.classificationSamples!.length}
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {item.classificationSamples!.map((sample: ClassificationSample) => (
                            <ClassificationSampleCard key={sample.doc_id} sample={sample} />
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
}


/* ------------------------------------------------------------------ */
/*  Classification Sample Card                                        */
/* ------------------------------------------------------------------ */

interface ClassificationSampleCardProps {
    sample: ClassificationSample;
}

function ClassificationSampleCard({ sample }: ClassificationSampleCardProps) {
    const [showFull, setShowFull] = useState(false);
    const badgeStyle = LABEL_BADGE_STYLES[sample.label] || LABEL_BADGE_STYLES.NEUTRAL;

    // Aggressively filter and deduplicate evidence spans
    const MAX_EVIDENCE_DISPLAY = 5;
    const seen = new Set<string>();
    const meaningfulSpans = (sample.evidence_spans || []).filter((span: string) => {
        const trimmed = span.trim();
        if (!trimmed || trimmed.length < 4) return false;
        // Filter placeholder text the LLM may return from schema examples
        if (/^exact quote/i.test(trimmed)) return false;
        if (/^<.*>$/.test(trimmed)) return false;
        // Do not filter by word count or single mentions (they are valid if the LLM outputted them)
        // Deduplicate (case-insensitive)
        const key = trimmed.toLowerCase();
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
    }).slice(0, MAX_EVIDENCE_DISPLAY);

    // Confidence color coding
    const confPct = sample.confidence * 100;
    const confColor = confPct >= 80 ? '#16a34a' : confPct >= 50 ? '#ca8a04' : '#dc2626';
    const confLabel = confPct >= 80 ? 'High' : confPct >= 50 ? 'Medium' : 'Low';

    return (
        <div style={{
            background: 'var(--bg-card)', borderRadius: 'var(--radius-sm)',
            padding: '10px 12px', border: '1px solid var(--neutral-200)',
        }}>
            {/* Header row */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', marginBottom: '8px', flexWrap: 'wrap' }}>
                {/* Label badge */}
                <span style={{
                    fontSize: '10px', fontWeight: 700, padding: '2px 6px',
                    borderRadius: '2px', background: badgeStyle.bg, color: badgeStyle.text,
                    textTransform: 'uppercase', letterSpacing: '0.06em',
                }}>
                    {sample.label}
                </span>
                {/* Confidence indicator */}
                <span
                    title={`Model confidence: ${confPct.toFixed(1)}% (${confLabel})`}
                    style={{
                        display: 'inline-flex', alignItems: 'center', gap: '4px',
                        fontSize: '10px', color: 'var(--neutral-500)',
                        fontFamily: 'var(--font-mono)', fontVariantNumeric: 'tabular-nums',
                    }}
                >
                    <span style={{ fontSize: '9px', opacity: 0.7, textTransform: 'uppercase', letterSpacing: '0.08em' }}>CONF</span>
                    <span style={{
                        display: 'inline-block', width: '36px', height: '3px',
                        borderRadius: '1px', background: 'var(--neutral-200)',
                        overflow: 'hidden', position: 'relative',
                    }}>
                        <span style={{
                            position: 'absolute', left: 0, top: 0, bottom: 0,
                            width: `${confPct}%`, background: confColor,
                            borderRadius: '1px', transition: 'width 0.3s ease',
                        }} />
                    </span>
                    <span style={{ color: confColor, fontWeight: 700 }}>
                        {confPct.toFixed(0)}%
                    </span>
                </span>
                {/* Sarcasm flag */}
                {sample.sarcasm_detected && (
                    <span style={{
                        fontSize: '9px', fontWeight: 700, padding: '2px 5px',
                        borderRadius: '2px', background: 'var(--semantic-warning-light)',
                        color: 'var(--semantic-warning)', letterSpacing: '0.08em',
                    }}>
                        SARCASM
                    </span>
                )}
                {/* Source info */}
                <span style={{
                    fontSize: '10px', color: 'var(--neutral-500)', marginLeft: 'auto',
                    display: 'flex', alignItems: 'center', gap: '6px',
                    fontFamily: 'var(--font-mono)',
                }}>
                    {(sample.source_name || sample.date) && (
                        <span>
                            {[sample.source_name, sample.date].filter(Boolean).join(' · ')}
                        </span>
                    )}
                    <span style={{
                        padding: '1px 5px', borderRadius: '2px',
                        background: 'var(--neutral-100)', textTransform: 'uppercase', letterSpacing: '0.06em',
                        fontWeight: 600,
                    }}>
                        {sample.source_type}
                    </span>
                </span>
            </div>

            {/* Title */}
            {sample.title && (
                <div style={{ fontSize: '12px', fontWeight: 500, marginBottom: '4px', lineHeight: 1.3 }}>
                    {sample.title.length > 100 ? sample.title.slice(0, 100) + '...' : sample.title}
                </div>
            )}

            {/* Reasoning (truncated with expand) */}
            <div style={{ fontSize: '12px', color: 'var(--neutral-700)', lineHeight: 1.5 }}>
                {showFull || sample.reasoning.length <= 150
                    ? sample.reasoning
                    : sample.reasoning.slice(0, 150) + '...'}
                {sample.reasoning.length > 150 && (
                    <button
                        onClick={() => setShowFull(!showFull)}
                        style={{
                            background: 'none', border: 'none', color: 'var(--accent)',
                            cursor: 'pointer', fontSize: '11px', fontWeight: 600,
                            marginLeft: '4px', padding: 0, textTransform: 'uppercase',
                            letterSpacing: '0.06em',
                        }}
                    >
                        {showFull ? 'Show less' : 'Show more'}
                    </button>
                )}
            </div>

            {/* Evidence spans - only meaningful, deduped, capped */}
            {meaningfulSpans.length > 0 && (
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px', marginTop: '8px' }}>
                    {meaningfulSpans.map((span: string, i: number) => (
                        <span
                            key={i}
                            title={span}
                            style={{
                                fontSize: '10px', padding: '2px 6px', borderRadius: '2px',
                                background: 'var(--neutral-100)',
                                color: 'var(--neutral-700)', fontStyle: 'italic',
                                maxWidth: '280px', overflow: 'hidden', textOverflow: 'ellipsis',
                                whiteSpace: 'nowrap', cursor: 'help',
                                borderLeft: `2px solid ${badgeStyle.text}`,
                            }}
                        >
                            &quot;{span}&quot;
                        </span>
                    ))}
                </div>
            )}

            {/* Source Text */}
            {sample.full_text && (
                <div style={{
                    marginTop: '10px', padding: '8px 10px', background: 'var(--bg-inset)',
                    borderRadius: '2px', borderLeft: '2px solid var(--neutral-300)',
                    fontSize: '11px', color: 'var(--neutral-700)',
                }}>
                    <div className="eyebrow" style={{
                        marginBottom: '6px',
                        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                    }}>
                        <span>Source Text</span>
                        {sample.url && (
                            <a href={sample.url} target="_blank" rel="noreferrer" style={{
                                color: 'var(--accent)', textDecoration: 'none',
                                textTransform: 'none', fontSize: '11px', letterSpacing: 'normal',
                            }}>
                                View Original ↗
                            </a>
                        )}
                    </div>
                    <div style={{ lineHeight: 1.5, whiteSpace: 'pre-wrap' }}>
                        {showFull || sample.full_text.length <= 150
                            ? sample.full_text
                            : sample.full_text.slice(0, 150) + '...'}
                    </div>
                </div>
            )}

            {/* Doc reference */}
            <div style={{
                marginTop: '6px', fontSize: '10px', color: 'var(--neutral-400)',
                fontFamily: 'var(--font-mono)', letterSpacing: '0.04em',
            }}>
                DOC #{sample.doc_id}
            </div>
        </div>
    );
}


/* ------------------------------------------------------------------ */
/*  Topic Sentiment Card (replaces flat-bar breakdown for topics)     */
/* ------------------------------------------------------------------ */

interface TopicSentimentCardProps {
    data: SentimentBreakdown[];
}

function TopicSentimentCard({ data }: TopicSentimentCardProps) {
    return (
        <Card
            title="Sentiment by Topic"
            headerActions={
                <MethodPopover
                    description="Topics are extracted via keyword matching. Each topic shows a donut chart of sentiment proportions, a net sentiment badge, and expandable LLM reasoning samples."
                    limitations={[
                        'Sentiment classification may miss sarcasm or irony (flagged when detected)',
                        'Sample may not be representative of all discourse',
                    ]}
                />
            }
        >
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                {data.map((item, i) => (
                    <TopicRow key={i} item={item} />
                ))}
            </div>
        </Card>
    );
}



interface SentimentDistributionCardProps {
    data: SentimentDistribution;
}

function SentimentDistributionCard({ data }: SentimentDistributionCardProps) {
    const total = Object.values(data).reduce((a, b) => a + b, 0) || 1;

    const segments = [
        { label: 'Strong unfavorable', value: data.strongNegative, color: '#991b1b' },
        { label: 'Mild unfavorable', value: data.mildNegative, color: '#dc2626' },
        { label: 'Neutral', value: data.neutral, color: '#9ca3af' },
        { label: 'Mild favorable', value: data.mildPositive, color: '#22c55e' },
        { label: 'Strong favorable', value: data.strongPositive, color: '#16a34a' },
    ];

    return (
        <Card
            title="Sentiment Distribution"
            subtitle="Including intensity levels"
            headerActions={
                <MethodPopover
                    description="Sentiment intensity classified using a 5-point scale based on lexical and contextual signals."
                    limitations={['Intensity thresholds are model-dependent']}
                />
            }
        >
            {/* Stacked bar */}
            <div
                style={{
                    display: 'flex',
                    height: '48px',
                    borderRadius: 'var(--radius-md)',
                    overflow: 'hidden',
                    marginBottom: 'var(--space-4)'
                }}
            >
                {segments.map((seg, i) => (
                    <div
                        key={i}
                        style={{
                            width: `${(seg.value / total) * 100}%`,
                            background: seg.color,
                            transition: 'width var(--transition-base)',
                        }}
                        title={`${seg.label}: ${seg.value}`}
                    />
                ))}
            </div>

            {/* Legend */}
            <div className="grid-2 gap-2">
                {segments.map((seg, i) => (
                    <div key={i} className="flex items-center gap-2">
                        <div
                            style={{
                                width: '12px',
                                height: '12px',
                                borderRadius: 'var(--radius-sm)',
                                background: seg.color
                            }}
                        />
                        <span className="text-sm">{seg.label}</span>
                        <span className="text-xs text-muted ml-auto">
                            {((seg.value / total) * 100).toFixed(1)}%
                        </span>
                    </div>
                ))}
            </div>
        </Card>
    );
}

interface SocialVsNewsCardProps {
    data: SocialVsNewsSentiment | null | undefined;
}

function SocialVsNewsCard({ data }: SocialVsNewsCardProps) {
    if (!data) {
        return (
            <Card title="Social Media vs News Outlets">
                <p className="text-muted text-sm">No comparison data available.</p>
            </Card>
        );
    }

    const formatNetScore = (score: number) => {
        const sign = score >= 0 ? '+' : '';
        return `${sign}${score.toFixed(1)}%`;
    };

    const getScoreColor = (score: number) => {
        if (score > 10) return '#16a34a';
        if (score < -10) return '#dc2626';
        return '#9ca3af';
    };

    return (
        <Card
            title="Sampled Social Media vs News Outlets"
            subtitle="Comparing sentiment between sampled social posts (Reddit + X) and news coverage"
            headerActions={
                <MethodPopover
                    description="The left column is drawn from sampled political discussions on Reddit and X — posts are not a statistical sample of the wider population, only of content we ingested through public APIs. The right column is news-article sentiment."
                    limitations={[
                        'Social sample over-represents engaged users and the specific subreddits / X queries we follow',
                        'News outlet sentiment may reflect editorial framing',
                        'Other platforms (TikTok, Facebook, private Discords, etc.) are not included',
                    ]}
                />
            }
        >
            <div className="grid-2 gap-6">
                {/* Sampled Social (Reddit + X) */}
                <div className="card" style={{ background: 'var(--neutral-50)', border: 'none', padding: 'var(--space-4)' }}>
                    <div className="flex justify-between items-center mb-3">
                        <span className="badge badge-accent">Sampled Social (Reddit + X)</span>
                        <span className="text-xs text-muted">{data.social.volume.toLocaleString()} items</span>
                    </div>
                    <div className="text-center mb-3">
                        <div className="text-2xl font-bold" style={{ color: getScoreColor(data.social.netScore) }}>
                            {formatNetScore(data.social.netScore)}
                        </div>
                        <div className="text-xs text-muted">Net Score</div>
                    </div>
                    <SentimentBar
                        positive={data.social.positive}
                        negative={data.social.negative}
                        neutral={data.social.neutral}
                        height={24}
                        showLabels={false}
                    />
                </div>

                {/* News Outlets */}
                <div className="card" style={{ background: 'var(--neutral-50)', border: 'none', padding: 'var(--space-4)' }}>
                    <div className="flex justify-between items-center mb-3">
                        <span className="badge badge-neutral">News Outlets</span>
                        <span className="text-xs text-muted">{data.news.volume.toLocaleString()} items</span>
                    </div>
                    <div className="text-center mb-3">
                        <div className="text-2xl font-bold" style={{ color: getScoreColor(data.news.netScore) }}>
                            {formatNetScore(data.news.netScore)}
                        </div>
                        <div className="text-xs text-muted">Net Score</div>
                    </div>
                    <SentimentBar
                        positive={data.news.positive}
                        negative={data.news.negative}
                        neutral={data.news.neutral}
                        height={24}
                        showLabels={false}
                    />
                </div>
            </div>

            {/* Disparity indicator */}
            {data.social.volume > 0 && data.news.volume > 0 && (
                <div className="card-note mt-4">
                    {Math.abs(data.social.netScore - data.news.netScore) > 20 ? (
                        <strong>Significant disparity detected:</strong>
                    ) : null}
                    {' '}Sampled social (Reddit + X) sentiment is {data.social.netScore > data.news.netScore ? 'more favorable' : 'less favorable'} than news coverage
                    by {Math.abs(data.social.netScore - data.news.netScore).toFixed(1)} percentage points.
                </div>
            )}
        </Card>
    );
}

function MethodTransparencyPanel() {
    return (
        <Card
            title="Methodology"
            note="This section explains how sentiment scores are derived and their limitations."
        >
            <div className="flex flex-col gap-4 text-sm">
                <div>
                    <h4 className="font-medium mb-1">Data Sources</h4>
                    <p className="text-muted">
                        Sentiment is derived from news articles, sampled Reddit discussions, and sampled X (Twitter) posts.
                        The social-media data is explicitly labeled as "sampled" because it only covers the specific subreddits
                        and X queries we ingest through public APIs — it does not represent the full population of online discourse.
                    </p>
                </div>

                <div>
                    <h4 className="font-medium mb-1">Classification Method</h4>
                    <p className="text-muted">
                        Text is classified by an LLM (Gemini or local Ollama) with evidence-span validation and per-sample
                        confidence scores. Classifications with unverifiable evidence are capped at low confidence.
                    </p>
                </div>

                <div>
                    <h4 className="font-medium mb-1">Known Limitations</h4>
                    <ul className="text-muted" style={{ margin: 0, paddingLeft: 'var(--space-5)' }}>
                        <li>Sarcasm and irony may be misclassified</li>
                        <li>Non-English content is excluded from analysis</li>
                        <li>Social media samples may over-represent highly engaged users</li>
                        <li>Sentiment does not equal opinion polling</li>
                    </ul>
                </div>
            </div>
        </Card>
    );
}

interface PublicSentimentProps {
    filters: Filters;
}

function PublicSentiment({ filters }: PublicSentimentProps) {
    const [data, setData] = useState<PublicSentimentData | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const loadData = async () => {
            setLoading(true);
            setError(null);
            try {
                const rawData = await fetchSentiment(filters.timeRange);
                const processedData = transformPublicSentiment(rawData);
                setData(processedData);
            } catch (err: any) {
                console.error("Failed to load sentiment:", err);
                setError(err.message || "Failed to load sentiment data.");
            } finally {
                setLoading(false);
            }
        };
        loadData();
    }, [filters]);

    if (error) {
        return <ErrorState message={error} onRetry={() => window.location.reload()} />;
    }


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

    if (!data) {
        return <EmptyState title="No sentiment data available" />;
    }

    return (
        <div className="flex flex-col gap-6">
            {/* Sampling disclaimer — invariant: never imply universal American sentiment */}
            <div
                style={{
                    padding: 'var(--space-3) var(--space-4)',
                    background: '#fffbeb',
                    border: '1px solid #fbbf24',
                    borderRadius: 'var(--radius-md)',
                    fontSize: 'var(--text-xs)',
                    color: '#92400e',
                }}
            >
                <strong>Sampled political discourse:</strong> sentiment reflects US political news articles plus
                sampled political Reddit and X discussions from the last 30 days. This is a snapshot of what the
                sources we ingest are saying about politics — not a scientific poll and not a representation of the
                full population.
            </div>

            {/* Overview Header */}
            <SentimentOverviewHeader data={data.overview} />

            {/* Social Media vs News Comparison */}
            <SocialVsNewsCard data={data.socialVsNews} />

            {/* Topic Sentiment (new design with reasoning) */}
            <TopicSentimentCard data={data.byTopic} />

            {/* Distribution */}
            <SentimentDistributionCard data={data.distribution} />

            {/* Time Window Breakdown */}
            <Card title="Sentiment by Time Window">
                <div className="grid-3">
                    {data.byTimeWindow.map((item: any, i: number) => (
                        <div key={i} className="card" style={{ background: 'var(--neutral-50)', border: 'none' }}>
                            <div className="text-sm font-medium mb-2">{item.window}</div>
                            <SentimentBar
                                positive={item.positive}
                                negative={item.negative}
                                neutral={item.neutral}
                                height={20}
                                showLabels={true}
                            />
                            <div className="text-xs text-muted mt-2">{item.volume.toLocaleString()} items</div>
                        </div>
                    ))}
                </div>
            </Card>

            {/* GOP Favorability (merged) */}
            {data.gopFavorability && (
                <GOPFavorabilityCard
                    favorability={data.gopFavorability}
                    trend={data.gopTrend}
                    pollingVsSocial={data.pollingVsSocial}
                />
            )}

            {/* Method Transparency */}
            <MethodTransparencyPanel />
        </div>
    );
}

interface GOPFavorabilityCardProps {
    favorability: NonNullable<PublicSentimentData['gopFavorability']>;
    trend: TrendPoint[] | null | undefined;
    pollingVsSocial: PollingSocialComparison | null | undefined;
}

function GOPFavorabilityCard({ favorability, trend, pollingVsSocial }: GOPFavorabilityCardProps) {
    const netColor = favorability.netFavorability > 0
        ? '#16a34a' : favorability.netFavorability < 0
            ? '#dc2626' : '#9ca3af';

    return (
        <Card
            title="GOP Favorability"
            subtitle={`Based on ${favorability.sampleSize.toLocaleString()} analyzed documents across ${favorability.sourceCount} platforms`}
            headerActions={
                <MethodPopover
                    description="GOP favorability is derived from stance indicators found in the same content analyzed for sentiment. Stance is determined via LLM classification with proximity matching to GOP entities."
                    limitations={['Stance may not reflect personal opinion of the author', 'Neutral stance may simply indicate factual reporting']}
                />
            }
        >
            {/* Net favorability hero metric */}
            <div className="text-center mb-4">
                <div className="text-3xl font-bold" style={{ color: netColor }}>
                    {favorability.netFavorability >= 0 ? '+' : ''}{favorability.netFavorability.toFixed(1)}%
                </div>
                <div className="text-xs text-muted">Net Favorability</div>
            </div>

            {/* Stance distribution using SentimentBar */}
            <div className="mb-4">
                <div className="flex justify-between items-center mb-2">
                    <span className="font-medium text-sm">Stance Distribution</span>
                    <span className="text-xs text-muted">{favorability.sampleSize.toLocaleString()} docs</span>
                </div>
                <SentimentBar
                    positive={favorability.favorable}
                    negative={favorability.unfavorable}
                    neutral={favorability.neutral}
                    height={32}
                    showLabels={true}
                />
            </div>

            {/* Trend chart */}
            {trend && trend.length > 0 && (
                <div className="mt-6 pt-4" style={{ borderTop: '1px solid var(--neutral-100)' }}>
                    <h4 className="font-medium text-sm mb-3">Favorability Trend</h4>
                    <TrendStrip
                        data={trend}
                        dataKey="value"
                        xKey="date"
                        height={160}
                        color={netColor}
                    />
                </div>
            )}

            {/* Polling comparison */}
            {pollingVsSocial && (
                <GOPPollingComparison data={pollingVsSocial} />
            )}
        </Card>
    );
}

function GOPPollingComparison({ data }: { data: PollingSocialComparison }) {
    return (
        <div className="mt-6 pt-4" style={{ borderTop: '1px solid var(--neutral-100)' }}>
            <h4 className="font-medium text-sm mb-3">Polling vs Online Sentiment</h4>
            <div className="grid-2 gap-4">
                <div className="card" style={{ background: 'var(--neutral-50)', border: 'none', padding: 'var(--space-3)' }}>
                    <div className="flex items-center gap-2 mb-2">
                        <span className="badge badge-accent">Online Sentiment</span>
                    </div>
                    <SentimentBar
                        positive={data.onlineSentiment?.favorable ?? 0}
                        negative={data.onlineSentiment?.unfavorable ?? 0}
                        neutral={data.onlineSentiment?.neutral ?? 0}
                        height={24}
                        showLabels={true}
                    />
                </div>
                <div className="card" style={{ background: 'var(--neutral-50)', border: 'none', padding: 'var(--space-3)' }}>
                    <div className="flex items-center gap-2 mb-2">
                        <span className="badge badge-neutral">Live Polling</span>
                        {data.pollingData?.source && (
                            <span style={{ fontSize: '10px', color: 'var(--text-muted)', marginLeft: 'auto' }}>
                                {data.pollingData.source}
                            </span>
                        )}
                    </div>
                    {data.pollingData ? (
                        <>
                            <SentimentBar
                                positive={data.pollingData.favorable}
                                negative={data.pollingData.unfavorable}
                                neutral={data.pollingData.neutral}
                                height={24}
                                showLabels={true}
                            />
                            {data.pollingData.date && (
                                <div style={{ fontSize: '10px', color: 'var(--text-muted)', marginTop: '4px', textAlign: 'right' }}>
                                    Last updated: {data.pollingData.date}
                                </div>
                            )}
                        </>
                    ) : (
                        <div style={{
                            padding: '8px 12px', borderRadius: 'var(--radius-sm)',
                            background: 'rgba(234,179,8,0.08)', border: '1px solid rgba(234,179,8,0.2)',
                        }}>
                            <p className="text-muted text-xs" style={{ margin: 0 }}>
                                Live polling data is currently unavailable. This may be due to a network issue or a change in the source page structure.
                            </p>
                        </div>
                    )}
                </div>
            </div>
            <div className="card-note mt-3">
                Online sentiment is derived from sampled social media discourse and should not be interpreted as equivalent to scientific polling data.
            </div>
        </div>
    );
}

export default PublicSentiment;
