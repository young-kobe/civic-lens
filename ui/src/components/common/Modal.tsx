import { useEffect, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

interface ModalProps {
    isOpen: boolean;
    onClose: () => void;
    title: string;
    /** Small uppercase category line above the title (e.g. "Narrative").
     *  Takes the accent color; the title itself stays ink for legibility. */
    kicker?: string;
    subtitle?: string;
    /** Optional accent color applied to the left border + title — used by
     *  callers that want to signal which bucket / segment the modal belongs to. */
    accentColor?: string;
    /** Control maximum width; defaults to 860px so a docs table can breathe. */
    maxWidth?: number;
    /** When provided, renders a ← arrow in the header that invokes this handler
     *  (plus a "Back to <backLabel>" tooltip). Used for nested drill-downs where
     *  closing should return to the parent modal, not dismiss the chain. */
    onBack?: () => void;
    /** Label describing what Back returns to — shown in the button's title /
     *  aria-label (e.g. "Back to NYT"). Defaults to "Back". */
    backLabel?: string;
    children: ReactNode;
}

/**
 * Centered dialog rendered into a portal. Escape and backdrop click both
 * close it; body scroll is locked while open so the page doesn't shift.
 *
 * The title is a real h2 at display scale — the dialog's own name must
 * outrank the h3 section headers inside it (the pre-2026-07-11 shell set
 * the title in an 11px grey eyebrow, which inverted the hierarchy on
 * every modal in the app).
 *
 * Accessibility notes:
 * - role="dialog" + aria-modal="true" on the surface
 * - aria-labelledby points at the h2 title; aria-describedby at the subtitle
 * - Initial focus lands on the close button so keyboard users have an
 *   obvious exit; content inside remains tab-navigable.
 */
export function Modal({
    isOpen,
    onClose,
    title,
    kicker,
    subtitle,
    accentColor,
    maxWidth = 860,
    onBack,
    backLabel,
    children,
}: ModalProps) {
    const closeRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        if (!isOpen) return;
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') onClose();
        };
        document.addEventListener('keydown', onKeyDown);
        const prevOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        closeRef.current?.focus();
        return () => {
            document.removeEventListener('keydown', onKeyDown);
            document.body.style.overflow = prevOverflow;
        };
    }, [isOpen, onClose]);

    if (!isOpen) return null;

    const titleId = 'modal-title';
    const subtitleId = subtitle ? 'modal-subtitle' : undefined;

    return createPortal(
        <div
            className="modal-backdrop"
            onClick={onClose}
            role="presentation"
        >
            <div
                className="modal-surface"
                role="dialog"
                aria-modal="true"
                aria-labelledby={titleId}
                aria-describedby={subtitleId}
                onClick={(e) => e.stopPropagation()}
                style={{
                    maxWidth,
                    borderLeftColor: accentColor ?? 'transparent',
                }}
            >
                <header className="modal-header">
                    {onBack && (
                        <button
                            type="button"
                            className="modal-back"
                            onClick={onBack}
                            aria-label={backLabel ? `Back to ${backLabel}` : 'Back'}
                            title={backLabel ? `Back to ${backLabel}` : 'Back'}
                        >
                            <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden stroke="currentColor" strokeWidth="2" fill="none" strokeLinecap="round" strokeLinejoin="round">
                                <path d="M15 18L9 12l6-6" />
                            </svg>
                        </button>
                    )}
                    <div style={{ minWidth: 0, flex: 1 }}>
                        {kicker && (
                            <div className="modal-kicker" style={{ color: accentColor ?? 'var(--neutral-500)' }}>
                                {kicker}
                            </div>
                        )}
                        <h2 className="modal-title" id={titleId}>
                            {title}
                        </h2>
                        {subtitle && (
                            <div
                                id={subtitleId}
                                className="text-xs text-muted mt-1"
                                style={{ fontVariantNumeric: 'tabular-nums' }}
                            >
                                {subtitle}
                            </div>
                        )}
                    </div>
                    <button
                        ref={closeRef}
                        type="button"
                        className="modal-close"
                        onClick={onClose}
                        aria-label="Close"
                    >
                        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden stroke="currentColor" strokeWidth="2" fill="none" strokeLinecap="round">
                            <path d="M6 6l12 12M18 6L6 18" />
                        </svg>
                    </button>
                </header>
                <div className="modal-body">
                    {children}
                </div>
            </div>
        </div>,
        document.body,
    );
}

export default Modal;
