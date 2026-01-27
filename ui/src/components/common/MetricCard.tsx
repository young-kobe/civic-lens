

type DeltaDirection = 'positive' | 'negative' | 'neutral';

interface MetricCardProps {
    label?: string;
    value: string | number;
    delta?: string | number;
    deltaDirection?: DeltaDirection;
    subtitle?: string;
    className?: string;
}

/**
 * MetricCard - Displays a single KPI with trend indicator.
 */
function MetricCard({
    label,
    value,
    delta,
    deltaDirection = 'neutral',
    subtitle,
    className = ''
}: MetricCardProps) {
    const getDeltaClass = (): string => {
        switch (deltaDirection) {
            case 'positive': return 'metric-delta-positive';
            case 'negative': return 'metric-delta-negative';
            default: return 'metric-delta-neutral';
        }
    };

    const getDeltaSymbol = (): string => {
        switch (deltaDirection) {
            case 'positive': return '+';
            case 'negative': return '';
            default: return '';
        }
    };

    return (
        <div className={`card ${className}`}>
            {label && <div className="card-subtitle mb-2">{label}</div>}
            <div className="metric-value">{value}</div>
            {delta !== undefined && (
                <div className={`metric-delta mt-2 ${getDeltaClass()}`}>
                    <span>{getDeltaSymbol()}{delta}</span>
                    {subtitle && <span className="text-muted"> {subtitle}</span>}
                </div>
            )}
        </div>
    );
}

export default MetricCard;
