import type { LeanLabel as LeanLabelData } from '../../types';
import { leanClass } from '../../theme';

// --------------------------------------------------------------------------- //
//  LeanLabel — the ONE place a political lean renders anywhere on the site.   //
//                                                                             //
//  The three kinds are verbally distinct on purpose (owner decision          //
//  2026-07-24, mirrors analysis/src/api/models/common.py::LeanLabel):        //
//    fact    -> "Party: <Value>"        (official party registration)       //
//    curated -> "Media lean: <Value>"   (registry editorial judgment)       //
//    derived -> "Content leans <Value>" + its evidence, ALWAYS shown        //
//               together — a derived lean never renders without the         //
//               share/confidence/sample count that backs it.                //
// --------------------------------------------------------------------------- //

// Every lean value on the wire is one of corpus.political_lean's five words
// (democrat/republican/independent/mixed/unknown) -- the single 5-value enum
// every curated AND derived lean is flattened onto (0001_north_star.sql).
function titleCase(value: string): string {
    return value.charAt(0).toUpperCase() + value.slice(1);
}

function factText(value: string): string {
    return `Party: ${titleCase(value)}`;
}

function curatedText(value: string): string {
    return `Media lean: ${titleCase(value)}`;
}

function derivedText(value: string): string {
    return `Content leans ${titleCase(value)}`;
}

interface LeanLabelProps {
    lean: LeanLabelData;
    /** Renders as a small chip (entity cards, ranked lists) instead of the
     *  full sentence + evidence line (modals, detail views). */
    variant?: 'chip' | 'full';
}

/** One-directional share phrasing: a `leanShare` of 0.72 toward the label's
 *  own value reads as "72% of scored posts leaned <value>". */
function evidenceLine(lean: LeanLabelData): string | null {
    if (lean.kind !== 'derived') return null;
    const parts: string[] = [];
    if (lean.leanShare != null) {
        parts.push(`${Math.round(lean.leanShare * 100)}% of scored posts leaned ${lean.value}`);
    }
    if (lean.sampleCount != null) {
        parts.push(`${lean.sampleCount.toLocaleString()} posts`);
    }
    if (lean.confidence != null) {
        parts.push(`confidence ${Math.round(lean.confidence * 100)}%`);
    }
    return parts.length > 0 ? parts.join(' · ') : null;
}

export function LeanLabel({ lean, variant = 'full' }: LeanLabelProps) {
    const cls = leanClass(lean.value);
    const text = lean.kind === 'fact'
        ? factText(lean.value)
        : lean.kind === 'curated'
            ? curatedText(lean.value)
            : derivedText(lean.value);
    const evidence = evidenceLine(lean);

    if (variant === 'chip') {
        return (
            <span
                className={`entity-card-chip lean-chip-${cls}`}
                title={evidence ? `${text} (${evidence})` : text}
            >
                {text}
            </span>
        );
    }

    return (
        <span className="lean-label">
            <span className={`entity-card-chip lean-chip-${cls}`}>{text}</span>
            {evidence && <span className="text-xs text-muted lean-label-evidence"> — {evidence}</span>}
        </span>
    );
}

export default LeanLabel;
