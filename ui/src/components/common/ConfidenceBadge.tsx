import { useState } from 'react';
import type { ConfidenceLevel, CoverageLevel } from '../../types';

interface ConfidenceBadgeProps {
    coverage?: CoverageLevel;
    confidence?: ConfidenceLevel;
    sampleSize?: number;
    sourceCount?: number;
    showTooltip?: boolean;
}

/**
 * ConfidenceBadge - Trust indicator showing coverage and confidence levels.
 */
function ConfidenceBadge({
    coverage = 'medium',
    confidence = 'medium',
    sampleSize,
    sourceCount,
    showTooltip = true
}: ConfidenceBadgeProps) {
    const [isOpen, setIsOpen] = useState(false);

    const getLabel = (level: string): string => {
        switch (level) {
            case 'high': return 'High';
            case 'medium': return 'Medium';
            case 'low': return 'Low';
            default: return level;
        }
    };

    return (
        <div
            className="confidence-indicator"
            role="status"
            aria-label={`Data quality: ${getLabel(coverage)} coverage, ${getLabel(confidence)} confidence`}
            style={{ position: 'relative' }}
        >
            <span aria-hidden className={`confidence-dot confidence-${confidence}`} />
            <span>
                {getLabel(coverage)} coverage / {getLabel(confidence)} confidence
            </span>
            {showTooltip && (sampleSize || sourceCount) && (
                <button
                    className="btn-ghost btn-sm"
                    onClick={() => setIsOpen(!isOpen)}
                    aria-label="More information"
                    style={{ marginLeft: '4px', padding: '2px 6px' }}
                >
                    ?
                </button>
            )}
            {isOpen && (
                <div className="popover" style={{ top: '100%', left: 0, marginTop: '8px' }}>
                    <div className="popover-title">Data Quality</div>
                    {sampleSize && <p>Sample size: {sampleSize.toLocaleString()}</p>}
                    {sourceCount && <p>Sources: {sourceCount}</p>}
                    <p className="text-muted mt-2">
                        Coverage indicates breadth of data sources. Confidence reflects analytical certainty.
                    </p>
                </div>
            )}
        </div>
    );
}

export default ConfidenceBadge;
