interface SentimentBarProps {
    positive?: number;
    negative?: number;
    neutral?: number;
    height?: number;
    showLabels?: boolean;
    /** Use political color scheme: blue for favorable (left), red for unfavorable (right) */
    politicalColors?: boolean;
}

/**
 * SentimentBar - Horizontal bar showing sentiment/favorability distribution.
 * Uses red (Republican-leaning) and blue (Democrat-leaning) color scheme with grey for neutral.
 */
function SentimentBar({
    positive = 0,
    negative = 0,
    neutral = 0,
    height = 40,
    showLabels = true,
    politicalColors = true
}: SentimentBarProps) {
    const total = positive + negative + neutral || 1;

    // Political color scheme: Blue = Democrat/Favorable, Red = Republican/Unfavorable
    const colors = politicalColors
        ? {
            positive: '#2563eb', // Blue (Democrat-leaning / Favorable)
            neutral: '#9ca3af',  // Grey
            negative: '#dc2626', // Red (Republican-leaning / Unfavorable)
        }
        : {
            positive: 'var(--semantic-positive)',
            neutral: 'var(--neutral-300)',
            negative: 'var(--semantic-negative)',
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
                        <span className="text-muted" style={{ marginLeft: '4px', fontWeight: 400 }}>(R)</span>
                    </span>
                    <span style={{ color: colors.neutral }}>
                        {((neutral / total) * 100).toFixed(0)}% neutral
                    </span>
                    <span style={{ color: colors.positive, fontWeight: 500 }}>
                        {((positive / total) * 100).toFixed(0)}% favorable
                        <span className="text-muted" style={{ marginLeft: '4px', fontWeight: 400 }}>(D)</span>
                    </span>
                </div>
            )}
        </div>
    );
}

export default SentimentBar;
