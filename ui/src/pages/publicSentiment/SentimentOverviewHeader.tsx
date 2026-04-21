import { Card, ConfidenceBadge } from '../../components/common';
import type { SentimentOverview } from '../../types';

interface SentimentOverviewHeaderProps {
    data: SentimentOverview;
}

function getScoreDisplay(score: number) {
    if (score > 0.1) return { label: 'Positive', class: 'metric-delta-positive' };
    if (score < -0.1) return { label: 'Negative', class: 'metric-delta-negative' };
    return { label: 'Neutral', class: 'metric-delta-neutral' };
}

/**
 * Compact stat row: net score, volume, confidence.
 * Previously used justify-between with two widely-spaced columns, which
 * left a large whitespace gap on wide desktop viewports. The grid layout
 * packs the three stats tightly and ConfidenceBadge sits inline instead
 * of stacking below a horizontal rule.
 */
export function SentimentOverviewHeader({ data }: SentimentOverviewHeaderProps) {
    const scoreInfo = getScoreDisplay(data.netScore);

    return (
        <Card>
            <div
                style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
                    gap: 'var(--space-4)',
                    alignItems: 'center',
                }}
            >
                <div>
                    <div className="eyebrow mb-1">Net Sentiment</div>
                    <div className="flex items-baseline gap-2">
                        <span className={`metric-value-lg ${scoreInfo.class}`}>
                            {data.netScore >= 0 ? '+' : ''}{data.netScore.toFixed(1)}%
                        </span>
                        <span className={`text-xs font-bold uppercase ${scoreInfo.class}`}>
                            {scoreInfo.label}
                        </span>
                    </div>
                </div>

                <div>
                    <div className="eyebrow mb-1">Scored Docs</div>
                    <div
                        className="num"
                        style={{
                            fontSize: 'var(--text-3xl)',
                            fontWeight: 600,
                            letterSpacing: '-0.02em',
                            lineHeight: 1,
                        }}
                    >
                        {data.volume.toLocaleString()}
                    </div>
                </div>

                <div
                    style={{
                        paddingLeft: 'var(--space-4)',
                        borderLeft: '1px solid var(--neutral-200)',
                    }}
                >
                    <div className="eyebrow mb-1">Coverage &amp; Confidence</div>
                    <ConfidenceBadge
                        coverage={data.coverage}
                        confidence={data.confidence}
                        sampleSize={data.volume}
                    />
                </div>
            </div>
        </Card>
    );
}
