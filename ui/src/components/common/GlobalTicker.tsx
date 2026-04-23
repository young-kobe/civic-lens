import type { CSSProperties, ReactNode } from 'react';
import { COLORS } from '../../theme';

export type TickerTone = 'positive' | 'negative' | 'neutral' | 'accent' | 'warning';

export interface TickerItem {
    label: string;
    /**
     * The main value. Strings are rendered verbatim (caller formats the unit,
     * sign, etc.); nodes let callers drop in richer marks (badges, dots).
     */
    value: ReactNode;
    /**
     * Optional trailing hint — small-caps grey text printed after the value
     * (e.g. "docs", "of 1,203", "Positive"). Keeps the value column monospaced
     * without fighting the unit label for typographic weight.
     */
    hint?: string;
    tone?: TickerTone;
    /** Make this item's value render at text-xl instead of text-base. */
    emphasis?: boolean;
    /** Optional screen-reader description layered via `aria-label`. */
    ariaLabel?: string;
}

interface GlobalTickerProps {
    /** Stats in the order they should read, left to right. */
    items: TickerItem[];
    /**
     * Optional timestamp string — rendered right-aligned. Meant to be the
     * last-refresh timestamp of the underlying snapshot. Leave undefined if
     * the page has no single timestamp to show.
     */
    refreshed?: string;
    /**
     * Optional left-border accent color (CSS var reference or hex). Draws a
     * 3px color band on the left edge of the strip — used on Overall Tone
     * to signal the window's net direction.
     */
    accentColor?: string;
    /** Accessible label for the whole strip. */
    ariaLabel?: string;
}

function toneColor(tone: TickerTone | undefined): string | undefined {
    switch (tone) {
        case 'positive': return COLORS.positive;
        case 'negative': return COLORS.negative;
        case 'accent':   return COLORS.accent;
        case 'warning':  return COLORS.warning;
        case 'neutral':
        default:
            return undefined;
    }
}

/**
 * GlobalTicker — editorial stat strip at the top of every data page.
 *
 * Replaces page-specific overview headers (was SentimentOverviewHeader on the
 * Overall Tone page; other pages had no strip at all). Callers pass an array
 * of labeled values; the ticker handles layout, dividers, and typographic
 * treatment. Keep each label terse — this is a glance, not a table.
 */
export function GlobalTicker({ items, refreshed, accentColor, ariaLabel }: GlobalTickerProps) {
    const style: CSSProperties = accentColor
        ? { borderLeftColor: accentColor }
        : {};
    const className = accentColor
        ? 'global-ticker global-ticker-accent'
        : 'global-ticker';

    return (
        <div
            className={className}
            role="group"
            aria-label={ariaLabel ?? 'Overview ticker'}
            style={style}
        >
            {items.map((item, i) => {
                const color = toneColor(item.tone);
                const valueStyle: CSSProperties = color ? { color } : {};
                const valueClass = item.emphasis
                    ? 'global-ticker-value global-ticker-value-lg'
                    : 'global-ticker-value';
                return (
                    <span
                        key={`${item.label}-${i}`}
                        className="global-ticker-item"
                        aria-label={item.ariaLabel}
                    >
                        <span className="global-ticker-label">{item.label}</span>
                        <span className={valueClass} style={valueStyle}>
                            {item.value}
                        </span>
                        {item.hint && (
                            <span
                                className="global-ticker-hint"
                                style={color ? { color } : undefined}
                            >
                                {item.hint}
                            </span>
                        )}
                        {i < items.length - 1 && <span aria-hidden className="global-ticker-sep" />}
                    </span>
                );
            })}
            {refreshed && (
                <span className="global-ticker-timestamp" aria-label={`Refreshed ${refreshed}`}>
                    <span className="tick-live" aria-hidden />
                    <span>{refreshed}</span>
                </span>
            )}
        </div>
    );
}

export default GlobalTicker;
