import type { RangeMeta } from '../../types';
import { formatCount } from '../../services/format';

// --------------------------------------------------------------------------- //
//  RangeCaption — the RangeMeta honesty block, rendered as a small           //
//  unobtrusive caption under a panel's header. Every aggregate response      //
//  carries `range`; this is the one place its sampled-vs-official-record     //
//  split and multi-model-version caveat get spelled out (owner decision      //
//  2026-07-24). Must be visible without interaction — a caption, not a       //
//  tooltip.                                                                  //
// --------------------------------------------------------------------------- //

export function RangeCaption({ range }: { range: RangeMeta }) {
    const total = range.sampledDocCount + range.officialRecordDocCount;
    if (total === 0 && range.modelIds.length <= 1) return null;

    const parts: string[] = [
        `${formatCount(range.sampledDocCount)} sampled`,
    ];
    if (range.officialRecordDocCount > 0) {
        parts.push(`${formatCount(range.officialRecordDocCount)} official record`);
    }

    return (
        <p className="range-caption text-xs text-muted">
            {parts.join(' · ')}
            {range.modelIds.length > 1 && (
                <span className="range-caption-caveat">
                    {' '}· spans {range.modelIds.length} model versions ({range.modelIds.join(', ')}) —
                    not directly comparable across the full range
                </span>
            )}
        </p>
    );
}

export default RangeCaption;
