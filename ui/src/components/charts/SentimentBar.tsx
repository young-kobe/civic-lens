interface SentimentBarProps {
    positive?: number;
    negative?: number;
    neutral?: number;
    height?: number;
    showLabels?: boolean;
    /** Color scheme: 'sentiment' (green/grey/red) or 'political' (blue/grey/red) */
    colorScheme?: 'sentiment' | 'political';
}

/**
 * SentimentBar - Horizontal bar showing sentiment/favorability distribution.
 * Default uses semantic colors: green (favorable), grey (neutral), red (unfavorable).
 */
function SentimentBar({
    positive = 0,
    negative = 0,
    neutral = 0,
    height = 40,
    showLabels = true,
    colorScheme = 'sentiment'
}: SentimentBarProps) {
    const total = positive + negative + neutral || 1;

    // Color schemes: semantic (green/red) or political (blue/red)
    const colors = colorScheme === 'political'
        ? {
            positive: '#2563eb', // Blue
            neutral: '#9ca3af',  // Grey
            negative: '#dc2626', // Red
        }
        : {
            positive: '#16a34a', // Green (favorable)
            neutral: '#9ca3af',  // Grey (neutral)
            negative: '#dc2626', // Red (unfavorable)
        };

    return (
        <div>
            <div style={{ display: 'flex', height, borderRadius: 'var(--radius-md)', overflow: 'hidden' }}>
                {negative > 0 && (
                    <div
                        style={{
                            width: `${(negative / total) * 100}%`,
                            background: colors.negative,
                            transition: 'width var(--transition-base)'
                        }}
                        title={`Unfavorable: ${negative}`}
                    />
                )}
                {neutral > 0 && (
                    <div
                        style={{
                            width: `${(neutral / total) * 100}%`,
                            background: colors.neutral,
                            transition: 'width var(--transition-base)'
                        }}
                        title={`Neutral: ${neutral}`}
                    />
                )}
                {positive > 0 && (
                    <div
                        style={{
                            width: `${(positive / total) * 100}%`,
                            background: colors.positive,
                            transition: 'width var(--transition-base)'
                        }}
                        title={`Favorable: ${positive}`}
                    />
                )}
            </div>

            {showLabels && (
                <div className="flex justify-between mt-2 text-xs">
                    <span style={{ color: colors.negative, fontWeight: 500 }}>
                        {((negative / total) * 100).toFixed(0)}% unfavorable
                    </span>
                    <span style={{ color: colors.neutral }}>
                        {((neutral / total) * 100).toFixed(0)}% neutral
                    </span>
                    <span style={{ color: colors.positive, fontWeight: 500 }}>
                        {((positive / total) * 100).toFixed(0)}% favorable
                    </span>
                </div>
            )}
        </div>
    );
}

export default SentimentBar;
