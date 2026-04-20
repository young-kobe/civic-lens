import { Card, MethodPopover } from '../../components/common';
import type { SentimentDistribution } from '../../types';

interface SentimentDistributionCardProps {
    data: SentimentDistribution;
}

// Local 5-point intensity scale — distinct from the 3-way positive/negative/
// neutral palette. The darker/lighter variants here aren't represented in
// the semantic tokens because they're only used in this one chart.
const SEGMENT_COLORS = {
    strongNegative: '#991b1b',
    mildNegative: '#dc2626',
    neutral: '#9ca3af',
    mildPositive: '#22c55e',
    strongPositive: '#16a34a',
} as const;

export function SentimentDistributionCard({ data }: SentimentDistributionCardProps) {
    const total = Object.values(data).reduce((a, b) => a + b, 0) || 1;

    const segments = [
        { label: 'Strong unfavorable', value: data.strongNegative, color: SEGMENT_COLORS.strongNegative },
        { label: 'Mild unfavorable', value: data.mildNegative, color: SEGMENT_COLORS.mildNegative },
        { label: 'Neutral', value: data.neutral, color: SEGMENT_COLORS.neutral },
        { label: 'Mild favorable', value: data.mildPositive, color: SEGMENT_COLORS.mildPositive },
        { label: 'Strong favorable', value: data.strongPositive, color: SEGMENT_COLORS.strongPositive },
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
            <div
                role="img"
                aria-label={`Sentiment distribution: ${segments.map(s => `${s.label} ${((s.value / total) * 100).toFixed(0)}%`).join(', ')}`}
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

            <div className="grid-2 gap-2">
                {segments.map((seg, i) => (
                    <div key={i} className="flex items-center gap-2">
                        <div
                            aria-hidden
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
