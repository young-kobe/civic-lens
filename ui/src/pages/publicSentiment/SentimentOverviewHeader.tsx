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

export function SentimentOverviewHeader({ data }: SentimentOverviewHeaderProps) {
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
